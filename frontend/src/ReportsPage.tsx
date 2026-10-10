import { useState } from 'react';
import { makeExcelReport } from './excelExport';

type Transaction = {
  id: string; description: string; amount: number; date: string;
  type: 'income' | 'expense'; category: string; isPaid: boolean | null;
};
type Api = (path: string) => Promise<unknown>;
type Props = { selectedMonth: string; api: Api };
type Period = 'month' | 'year';
type Kind = 'all' | 'income' | 'expense';
type Format = 'pdf' | 'txt' | 'xlsx';

const money = (n: number) => n.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
const status = (t: Transaction) => t.type === 'income' ? '—' : t.isPaid === true ? 'Paga' : t.isPaid === false ? 'Não paga' : 'Não informado';
const escapeHtml = (s: string) => s.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c] ?? c);
const description = (period: Period, month: string, year: string) => period === 'month' ? month.split('-').reverse().join('/') : year;
const total = (rows: Transaction[], kind: Transaction['type']) => rows.filter(x => x.type === kind).reduce((sum, x) => sum + x.amount, 0);

function reportText(rows: Transaction[], label: string, kind: Kind) {
  const income = total(rows, 'income'), expense = total(rows, 'expense');
  const lines = [
    'MEU FINANCEIRO — RELATÓRIO DE LANÇAMENTOS',
    `Período: ${label}`,
    `Filtro: ${kind === 'all' ? 'Receitas e despesas' : kind === 'income' ? 'Somente receitas' : 'Somente despesas'}`,
    `Gerado em: ${new Date().toLocaleString('pt-BR')}`, '',
    `Receitas: ${money(income)}`, `Despesas: ${money(expense)}`,
    `Saldo: ${money(income - expense)}`, `Lançamentos: ${rows.length}`, '',
    'DATA | TIPO | DESCRIÇÃO | CATEGORIA | PAGAMENTO | VALOR',
    ...rows.map(x => `${x.date.split('-').reverse().join('/')} | ${x.type === 'income' ? 'Receita' : 'Despesa'} | ${x.description.replace(/[\r\n|]+/g, ' ')} | ${x.category.replace(/[\r\n|]+/g, ' ')} | ${status(x)} | ${money(x.amount)}`)
  ];
  return lines.join('\r\n');
}

function printHtml(rows: Transaction[], label: string, kind: Kind) {
  const income = total(rows, 'income'), expense = total(rows, 'expense');
  const trs = rows.map(t => `<tr><td>${escapeHtml(t.date.split('-').reverse().join('/'))}</td><td>${t.type === 'income' ? 'Receita' : 'Despesa'}</td><td>${escapeHtml(t.description)}</td><td>${escapeHtml(t.category)}</td><td>${status(t)}</td><td class="amount">${escapeHtml(money(t.amount))}</td></tr>`).join('');
  return `<!doctype html><html lang="pt-BR"><head><meta charset="UTF-8"/><title>Relatório financeiro - ${escapeHtml(label)}</title><style>
  @page{size:A4;margin:16mm}body{font:12px Arial,sans-serif;color:#243044}
  h1{font-size:23px;margin:0 0 7px}p{color:#64748b;margin:0 0 18px}.summary{display:flex;gap:20px;margin:20px 0}
  .summary div{padding:12px;border:1px solid #dce2eb;border-radius:8px;flex:1}
  .summary strong{display:block;font-size:16px;margin-top:7px}
  table{width:100%;border-collapse:collapse;font-size:10px}th{background:#edf3fa;text-align:left}
  td,th{border-bottom:1px solid #dce2eb;padding:8px 5px;overflow-wrap:anywhere}
  tr{break-inside:avoid}.amount{text-align:right;white-space:nowrap}
  footer{margin-top:20px;color:#64748b;font-size:10px}
  </style></head><body><h1>Meu Financeiro</h1>
  <p>Relatório de ${kind === 'all' ? 'receitas e despesas' : kind === 'income' ? 'receitas' : 'despesas'} — ${escapeHtml(label)}<br/>Gerado em ${escapeHtml(new Date().toLocaleString('pt-BR'))}</p>
  <div class="summary"><div>Receitas<strong>${escapeHtml(money(income))}</strong></div><div>Despesas<strong>${escapeHtml(money(expense))}</strong></div><div>Saldo<strong>${escapeHtml(money(income - expense))}</strong></div></div>
  <table><thead><tr><th>Data</th><th>Tipo</th><th>Descrição</th><th>Categoria</th><th>Pagamento</th><th>Valor</th></tr></thead><tbody>${trs || '<tr><td colspan="6">Nenhum lançamento encontrado.</td></tr>'}</tbody></table>
  <footer>${rows.length} lançamento(s). Relatório gerado para uso pessoal.</footer></body></html>`;
}

export function ReportsPage({ selectedMonth, api }: Props) {
  const [period, setPeriod] = useState<Period>('month');
  const [month, setMonth] = useState(selectedMonth);
  const [year, setYear] = useState(selectedMonth.slice(0, 4));
  const [kind, setKind] = useState<Kind>('all');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  async function download(format: Format) {
    if (busy) return;
    setError('');
    // Open synchronously from the click to avoid popup blockers after fetching.
    const printWindow = format === 'pdf' ? window.open('', '_blank') : null;
    if (format === 'pdf' && !printWindow) {
      setError('Permita a abertura da janela de impressão para salvar o relatório como PDF.');
      return;
    }
    if (printWindow) {
      printWindow.document.write('<!doctype html><title>Preparando relatório...</title><p>Preparando relatório...</p>');
      printWindow.document.close();
    }
    setBusy(true);
    try {
      const monthIds = period === 'month' ? [month] : Array.from({ length: 12 }, (_, i) => `${year}-${String(i + 1).padStart(2, '0')}`);
      const response = await Promise.all(monthIds.map(m => api(`/api/transactions?month=${m}`) as Promise<Transaction[]>));
      const rows = response.flat().filter(x => kind === 'all' || x.type === kind)
        .sort((a, b) => a.date.localeCompare(b.date) || a.description.localeCompare(b.description));
      const label = description(period, month, year);
      const filename = `financas-${period === 'month' ? month : year}-${kind}`;
      if (format === 'xlsx') {
        const blob = makeExcelReport(rows, label);
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = filename + '.xlsx';
        document.body.appendChild(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
      } else if (format === 'txt') {
        const blob = new Blob(['\uFEFF', reportText(rows, label, kind)], { type: 'text/plain;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = filename + '.txt';
        document.body.appendChild(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
      } else if (printWindow && !printWindow.closed) {
        printWindow.document.open();
        printWindow.document.write(printHtml(rows, label, kind));
        printWindow.document.close();
        printWindow.focus();
        printWindow.print();
      }
    } catch {
      printWindow?.close();
      setError('Não foi possível carregar os lançamentos. Verifique a conexão e tente novamente.');
    } finally {
      setBusy(false);
    }
  }

  return <section className="panel reports-page">
    <div className="panel-heading"><div><h2>Exportar relatórios</h2><p>Gere documentos com seus lançamentos financeiros.</p></div></div>
    <div className="report-fields">
      <div className="field"><label htmlFor="report-period">Período</label><select id="report-period" value={period} onChange={e => setPeriod(e.target.value as Period)}><option value="month">Por mês</option><option value="year">Por ano</option></select></div>
      {period === 'month' ? <div className="field"><label htmlFor="report-month">Mês de referência</label><input id="report-month" type="month" min="2000-01" max="2100-12" value={month} onChange={e => setMonth(e.target.value)}/></div>
        : <div className="field"><label htmlFor="report-year">Ano de referência</label><input id="report-year" type="number" min="2000" max="2100" value={year} onChange={e => setYear(e.target.value)}/></div>}
      <div className="field"><label htmlFor="report-kind">Lançamentos</label><select id="report-kind" value={kind} onChange={e => setKind(e.target.value as Kind)}><option value="all">Receitas e despesas</option><option value="income">Somente receitas</option><option value="expense">Somente despesas</option></select></div>
    </div>
    <div className="report-actions"><button className="primary" disabled={busy || (period === 'year' && (+year < 2000 || +year > 2100))} onClick={() => void download('pdf')}>Salvar como PDF</button><button className="secondary" disabled={busy} onClick={() => void download('txt')}>Baixar TXT</button><button className="secondary" disabled={busy} onClick={() => void download('xlsx')}>Baixar Excel (.xlsx)</button></div>
    <p className="report-hint">PDF: abre a impressão do navegador. Escolha “Salvar como PDF”. Os dados são obtidos da sua API autenticada e não são enviados a um serviço externo.</p>
    {busy && <p role="status">Preparando relatório...</p>}
    {error && <div role="alert" className="alert">{error}</div>}
  </section>;
}
