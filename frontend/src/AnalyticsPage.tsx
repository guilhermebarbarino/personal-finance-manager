import { useMemo } from 'react';
import { Bar, BarChart, CartesianGrid, Cell, Legend, Line, LineChart, Pie, PieChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';

type Transaction = { id: string; description: string; amount: number; date: string; type: 'income' | 'expense'; category: string };
type Month = { month: number; income: number; expenses: number; balance: number; name: string };
type Props = { rows: Transaction[]; series: Month[]; year: number; selectedMonth: string };

const brl = (value: number) => new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value);
const palette = ['#247bb5', '#20a57b', '#f0ad4e', '#a78bfa', '#eb7787', '#64748b', '#14b8a6', '#d97706'];

export function AnalyticsPage({ rows, series, year, selectedMonth }: Props) {
  const categories = useMemo(() => {
    const amounts = new Map<string, number>();
    for (const row of rows) {
      if (row.type === 'expense') amounts.set(row.category, (amounts.get(row.category) ?? 0) + row.amount);
    }
    return [...amounts.entries()]
      .map(([name, value]) => ({ name, value }))
      .sort((a, b) => b.value - a.value);
  }, [rows]);

  const runningBalance = useMemo(() => {
    let total = 0;
    return series.map(month => ({ name: month.name, balance: total += month.balance }));
  }, [series]);

  const annualIncome = series.reduce((sum, month) => sum + month.income, 0);
  const annualExpense = series.reduce((sum, month) => sum + month.expenses, 0);
  const monthLabel = selectedMonth.split('-').reverse().join('/');
  const axisFormat = (n: number) => n >= 1000 || n <= -1000 ? `R$ ${(n / 1000).toFixed(0)} mil` : `R$ ${n}`;

  return <div className="analytics-page">
    <div className="analysis-summary">
      <div><span>Receitas no ano</span><strong className="green">{brl(annualIncome)}</strong></div>
      <div><span>Despesas no ano</span><strong className="red">{brl(annualExpense)}</strong></div>
      <div><span>Saldo do ano</span><strong>{brl(annualIncome - annualExpense)}</strong></div>
    </div>
    <section className="panel">
      <div className="panel-heading"><div><h2>Despesas por categoria</h2><p>Distribuição dos gastos em {monthLabel}</p></div><span>Mês selecionado</span></div>
      {categories.length === 0
        ? <div className="chart-empty">Nenhuma despesa registrada neste mês.</div>
        : <div className="category-layout">
          <div className="chart-area">
            <ResponsiveContainer width="100%" height="100%">
              <PieChart><Pie data={categories} dataKey="value" nameKey="name" cx="50%" cy="50%" innerRadius={65} outerRadius={102} paddingAngle={2}>
                {categories.map((category, index) => <Cell key={category.name} fill={palette[index % palette.length]} />)}
              </Pie><Tooltip formatter={value => brl(Number(value))} /></PieChart>
            </ResponsiveContainer>
          </div>
          <div className="category-legend">
            {categories.map((category, index) => <div className="category-row" key={category.name}>
              <span className="category-dot" style={{ background: palette[index % palette.length] }} />
              <span>{category.name}</span><strong>{brl(category.value)}</strong>
            </div>)}
          </div>
        </div>}
    </section>
    <section className="panel">
      <div className="panel-heading"><div><h2>Comparativo mensal</h2><p>Receitas versus despesas durante {year}</p></div><span>{year}</span></div>
      <div className="chart-area analytics-chart"><ResponsiveContainer width="100%" height="100%">
        <BarChart data={series}><CartesianGrid vertical={false} stroke="#e9edf2" /><XAxis dataKey="name" tickLine={false} axisLine={false}/><YAxis tickFormatter={axisFormat} tickLine={false} axisLine={false} width={80}/><Tooltip formatter={value => brl(Number(value))}/><Legend/><Bar name="Receitas" dataKey="income" fill="#20a57b" radius={[5,5,0,0]}/><Bar name="Despesas" dataKey="expenses" fill="#eb7787" radius={[5,5,0,0]}/></BarChart>
      </ResponsiveContainer></div>
    </section>
    <section className="panel">
      <div className="panel-heading"><div><h2>Saldo acumulado</h2><p>Soma progressiva dos saldos mensais em {year}, iniciando em zero</p></div><span>{year}</span></div>
      <div className="chart-area analytics-chart"><ResponsiveContainer width="100%" height="100%">
        <LineChart data={runningBalance}><CartesianGrid vertical={false} stroke="#e9edf2"/><XAxis dataKey="name" tickLine={false} axisLine={false}/><YAxis tickFormatter={axisFormat} tickLine={false} axisLine={false} width={80}/><Tooltip formatter={value => brl(Number(value))}/><Line name="Saldo acumulado" dataKey="balance" type="monotone" stroke="#247bb5" strokeWidth={3} dot={{r:3}} activeDot={{r:6}}/></LineChart>
      </ResponsiveContainer></div>
    </section>
  </div>;
}
