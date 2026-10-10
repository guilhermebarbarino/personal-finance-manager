/**
 * Create a standards-compliant, uncompressed XLSX file entirely in the browser.
 * The first worksheet follows the application's import template.
 */
export type ExcelRow = {
  description: string;
  amount: number;
  date: string;
  type: 'income' | 'expense';
  category: string;
  isPaid: boolean | null;
};
const utf8 = new TextEncoder();
const xmlEscape = (s: string) => s.replace(/[&<>"']/g, c =>
  ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&apos;' })[c] || c
);
function crc32(bytes: Uint8Array): number {
  let c = 0xffffffff;
  for (const byte of bytes) {
    c ^= byte;
    for (let i = 0; i < 8; i++) c = (c >>> 1) ^ ((c & 1) ? 0xedb88320 : 0);
  }
  return (c ^ 0xffffffff) >>> 0;
}
function zip(files: { path: string; data: string }[]): Blob {
  const local: Uint8Array[] = [], central: Uint8Array[] = [];
  let offset = 0;
  const put16 = (v: DataView, o: number, n: number) => v.setUint16(o, n, true);
  const put32 = (v: DataView, o: number, n: number) => v.setUint32(o, n, true);
  for (const file of files) {
    const name = utf8.encode(file.path), data = utf8.encode(file.data), crc = crc32(data);
    const header = new Uint8Array(30 + name.length), h = new DataView(header.buffer);
    put32(h, 0, 0x04034b50); put16(h, 4, 20); put32(h, 14, crc);
    put32(h, 18, data.length); put32(h, 22, data.length);
    put16(h, 26, name.length); header.set(name, 30);
    local.push(header, data);
    const record = new Uint8Array(46 + name.length), v = new DataView(record.buffer);
    put32(v, 0, 0x02014b50); put16(v, 4, 20); put16(v, 6, 20);
    put32(v, 16, crc); put32(v, 20, data.length); put32(v, 24, data.length);
    put16(v, 28, name.length); put32(v, 42, offset); record.set(name, 46);
    central.push(record); offset += header.length + data.length;
  }
  const centralSize = central.reduce((n, v) => n + v.length, 0);
  const end = new Uint8Array(22), e = new DataView(end.buffer);
  put32(e, 0, 0x06054b50); put16(e, 8, files.length); put16(e, 10, files.length);
  put32(e, 12, centralSize); put32(e, 16, offset);
  return new Blob([...local, ...central, end], {
    type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'
  });
}
function cell(column: number, row: number, value: string | number): string {
  const ref = String.fromCharCode(65 + column) + row;
  return typeof value === 'number'
    ? `<c r="${ref}"><v>${value}</v></c>`
    : `<c r="${ref}" t="inlineStr"><is><t xml:space="preserve">${xmlEscape(value)}</t></is></c>`;
}
const headings = ['Descrição', 'Valor', 'Data', 'Tipo', 'Categoria', 'Pagamento'];
export function makeExcelReport(records: ExcelRow[], label: string): Blob {
  const data: (string | number)[][] = [
    headings,
    ...records.map(t => [
      t.description,
      t.amount,
      t.date,
      t.type === 'income' ? 'Receita' : 'Despesa',
      t.category,
      t.type === 'income' ? '' : t.isPaid === null ? '' : t.isPaid ? 'Paga' : 'Não paga'
    ])
  ];
  const rows = data.map((r, i) => `<row r="${i + 1}">${r.map((v, j) => cell(j, i + 1, v)).join('')}</row>`).join('');
  const sheet = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
    <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
    <cols><col min="1" max="1" width="36" customWidth="1"/><col min="2" max="2" width="17" customWidth="1"/>
    <col min="3" max="3" width="17" customWidth="1"/><col min="4" max="6" width="21" customWidth="1"/></cols>
    <sheetData>${rows}</sheetData></worksheet>`;
  const income = records.filter(x => x.type === 'income').reduce((a, x) => a + x.amount, 0);
  const expense = records.filter(x => x.type === 'expense').reduce((a, x) => a + x.amount, 0);
  const summary: (string | number)[][] = [
    ['Período', label], ['Receitas', income], ['Despesas', expense],
    ['Saldo', income - expense], ['Quantidade', records.length]
  ];
  const summaryRows = summary.map((r, i) => `<row r="${i + 1}">${r.map((v, j) => cell(j, i + 1, v)).join('')}</row>`).join('');
  const summarySheet = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
    <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
    <sheetData>${summaryRows}</sheetData></worksheet>`;
  return zip([
    { path: '[Content_Types].xml', data: `<?xml version="1.0" encoding="UTF-8"?>
      <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
      <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
      <Default Extension="xml" ContentType="application/xml"/>
      <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
      <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
      <Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
      </Types>` },
    { path: '_rels/.rels', data: `<?xml version="1.0" encoding="UTF-8"?>
      <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
      <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
      </Relationships>` },
    { path: 'xl/workbook.xml', data: `<?xml version="1.0" encoding="UTF-8"?>
      <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
      <sheets><sheet name="Lançamentos" sheetId="1" r:id="rId1"/><sheet name="Resumo" sheetId="2" r:id="rId2"/></sheets></workbook>` },
    { path: 'xl/_rels/workbook.xml.rels', data: `<?xml version="1.0" encoding="UTF-8"?>
      <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
      <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
      <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/>
      </Relationships>` },
    { path: 'xl/worksheets/sheet1.xml', data: sheet },
    { path: 'xl/worksheets/sheet2.xml', data: summarySheet }
  ]);
}
