import { useState } from 'react';
import { parseSheet, type Cell } from './excelReader';

type Transaction = { id:string; description:string;amount:number;date:string;type:'income'|'expense';category:string;isPaid:boolean|null };
type NewTransaction = Omit<Transaction,'id'>;
type Api = (path:string,init?:RequestInit)=>Promise<unknown>;
type Preview = { line:number; transaction?:NewTransaction; error?:string; duplicate?:boolean };
const headers=['Descrição','Valor','Data','Tipo','Categoria','Pagamento'];
const normalize=(v:unknown)=>String(v??'').normalize('NFD').replace(/[\u0300-\u036f]/g,'').trim().toLowerCase();
const key=(t:NewTransaction)=>[t.date,t.type,t.description.trim().toLowerCase(),t.amount.toFixed(2),t.category.trim().toLowerCase()].join('|');

function excelDate(n:number):string{
 return new Date(Date.UTC(1899,11,30)+Math.floor(n)*86400000).toISOString().slice(0,10);
}
function parseDate(cell:Cell|undefined):string {
 const v=String(cell??'').trim();
 if(typeof cell==='number'&&cell>20000&&cell<90000)return excelDate(cell);
 if(/^\d{4}-\d{2}-\d{2}$/.test(v))return v;
 const pt=v.match(/^(\d{1,2})\/(\d{1,2})\/(\d{4})$/);
 if(pt)return `${pt[3]}-${pt[2].padStart(2,'0')}-${pt[1].padStart(2,'0')}`;
 return '';
}
function parseAmount(cell:Cell|undefined):number {
 if(typeof cell==='number')return cell;
 let v=String(cell??'').trim().replace(/R\$\s*/gi,'').replace(/\s/g,'');
 if(v.includes(',')&&v.includes('.'))v=v.replace(/\./g,'').replace(',','.');
 else if(v.includes(','))v=v.replace(',','.');
 return v ? Number(v) : NaN;
}
function toRecord(cells:Cell[], indexes:number[]):NewTransaction {
 const [desc,amount,date,type,category,payment]=indexes.map(i=>i<0?undefined:cells[i]);
 const description=String(desc??'').trim(), value=parseAmount(amount), parsedDate=parseDate(date);
 const t=normalize(type),pay=normalize(payment);
 const transactionType=t==='receita'||t==='income'?'income':t==='despesa'||t==='expense'?'expense':null;
 const categoryValue=String(category??'').trim()||'Outros';
 if(!description)throw new Error('Descrição obrigatória.');
 if(description.length>150)throw new Error('Descrição deve ter até 150 caracteres.');
 if(!Number.isFinite(value)||value<=0||value>999999999999.99)throw new Error('Valor inválido. Informe um número positivo.');
 if(!parsedDate||Number.isNaN(Date.parse(parsedDate))||parsedDate<'2000-01-01'||parsedDate>'2100-12-31')throw new Error('Data inválida. Use DD/MM/AAAA ou AAAA-MM-DD.');
 if(!transactionType)throw new Error('Tipo inválido. Use Receita ou Despesa.');
 if(categoryValue.length>60)throw new Error('Categoria deve ter até 60 caracteres.');
 let isPaid:boolean|null=null;
 if(transactionType==='expense'){
  if(pay==='paga'||pay==='pago'||pay==='sim'||pay==='true'||pay==='1')isPaid=true;
  else if(pay==='nao paga'||pay==='nao pago'||pay==='nao'||pay==='false'||pay==='0')isPaid=false;
  else if(pay&&pay!=='nao informado'&&pay!=='null')throw new Error('Pagamento inválido. Use Paga, Não paga ou deixe vazio.');
 }
 return {description,amount:value,date:parsedDate,type:transactionType,category:categoryValue,isPaid};
}
export function ImportPage({api,onImported}:{api:Api;onImported:()=>Promise<void>}) {
 const [items,setItems]=useState<Preview[]>([]),[filename,setFilename]=useState('');
 const [busy,setBusy]=useState(false),[feedback,setFeedback]=useState(''),[error,setError]=useState('');
 const [targetMonth,setTargetMonth]=useState(new Date().toISOString().slice(0,7));
 const [useTargetMonth,setUseTargetMonth]=useState(false);
 async function readFile(file:File|undefined) {
  if(!file)return;
  setError('');setFeedback('');setItems([]);setFilename(file.name);setBusy(true);
  try{
   const data=await parseSheet(file);
   if(data.length<2)throw new Error('A planilha precisa ter cabeçalho e pelo menos uma linha.');
   if(data.length>501)throw new Error('Importe no máximo 500 lançamentos por arquivo.');
   const columns=data[0].map(normalize);
   const indexes=headers.map(x=>columns.indexOf(normalize(x)));
   for(let i=0;i<4;i++)if(indexes[i]<0)throw new Error(`Coluna obrigatória ausente: ${headers[i]}.`);
   const result=data.slice(1).map((row,i):Preview=>{
    try{
     const record=toRecord(row,indexes);
     if(useTargetMonth){
      const day=Number(record.date.slice(-2)),year=Number(targetMonth.slice(0,4)),month=Number(targetMonth.slice(-2));
      const last=new Date(Date.UTC(year,month,0)).getUTCDate();
      record.date=`${targetMonth}-${String(Math.min(day,last)).padStart(2,'0')}`;
     }
     return {line:i+2,transaction:record};
    }catch(e){return {line:i+2,error:(e as Error).message};}
   });
   const seen=new Set<string>();
   for(const r of result)if(r.transaction){const id=key(r.transaction);if(seen.has(id)){r.duplicate=true;r.error='Lançamento repetido nesta planilha.';}else seen.add(id);}
   setItems(result);
  }catch(e){setError((e as Error).message);}finally{setBusy(false);}
 }
 // Recompute when choosing the target month by asking for a new file selection.
 const valid=items.filter(x=>x.transaction&&!x.error);
 const invalid=items.filter(x=>x.error);
 async function importRecords() {
  if(!valid.length||busy)return;
  setBusy(true);setError('');setFeedback('');
  let saved=0,skipped=0;const failures:string[]=[];
  try{
   const months=[...new Set(valid.map(x=>x.transaction!.date.slice(0,7)))];
   const existing=new Set<string>();
   for(const month of months){
    const entries=await api(`/api/transactions?month=${month}`) as Transaction[];
    entries.forEach(x=>existing.add(key(x)));
   }
   const pending=valid.filter(x=>{if(existing.has(key(x.transaction!))){skipped++;return false;}return true;});
   if(!pending.length){setFeedback(`Nenhum registro novo. ${skipped} já existia(m) no sistema.`);return;}
   if(!window.confirm(`Confirmar importação de ${pending.length} lançamento(s)? ${skipped} duplicado(s) será(ão) ignorado(s).`))return;
   for(const item of pending){
    try{
     await api('/api/transactions',{method:'POST',body:JSON.stringify(item.transaction)});
     saved++;
    }catch(e){
     failures.push(`Linha ${item.line}: ${(e as Error).message}`);
    }
   }
   setFeedback(`${saved} lançamento(s) importado(s). ${skipped} duplicado(s) ignorado(s). ${failures.length} erro(s).`);
   if(failures.length)setError(failures.slice(0,8).join(' | '));
   if(saved)await onImported();
   setItems([]);
  }catch(e){setError(`Não foi possível consultar lançamentos existentes: ${(e as Error).message}`);}
  finally{setBusy(false);}
 }
 function template(){
  const data=[headers.join(';'),'Aluguel;1200,00;10/10/2026;Despesa;Moradia;Não paga','Salário;3500,00;05/10/2026;Receita;Trabalho;'];
  const blob=new Blob(['\uFEFF',data.join('\r\n')],{type:'text/csv;charset=utf-8'});
  const url=URL.createObjectURL(blob),a=document.createElement('a');a.href=url;a.download='modelo-importacao-financas.csv';document.body.appendChild(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),1000);
 }
 return <section className="panel import-page">
  <div className="panel-heading"><div><h2>Importar lançamentos do Excel</h2><p>Selecione um arquivo .xlsx ou .csv. Confira os dados antes de importar.</p></div></div>
  <div className="report-actions"><button type="button" className="secondary" onClick={template}>Baixar modelo para Excel</button></div>
  <div className="import-options">
   <label><input type="checkbox" checked={useTargetMonth} onChange={e=>{setUseTargetMonth(e.target.checked);setItems([]);setFeedback('');}}/> Usar as datas da planilha no mês selecionado (para repetir no mês seguinte)</label>
   {useTargetMonth&&<input aria-label="Mês de destino" type="month" min="2000-01" max="2100-12" value={targetMonth} onChange={e=>{setTargetMonth(e.target.value);setItems([]);}}/>}
   <small>Ao alterar o mês de destino, selecione o arquivo novamente. Datas acima do último dia do mês são ajustadas automaticamente.</small>
  </div>
  <div className="field"><label htmlFor="import-file">Planilha Excel (.xlsx ou .csv)</label><input id="import-file" type="file" accept=".xlsx,.csv" onChange={e=>void readFile(e.target.files?.[0])}/></div>
  <p className="report-hint">Obrigatórios: Descrição, Valor, Data e Tipo. Categoria vazia recebe “Outros”. Pagamento vazio é registrado como não informado (NULL). Nenhuma informação é enviada a terceiros para ler a planilha.</p>
  {filename&&<p className="report-hint">Arquivo: {filename}</p>}
  {!!items.length&&<><div className="import-stats"><strong>{valid.length} pronto(s)</strong><strong>{invalid.length} com problema(s)</strong></div>
   <div className="table-wrap import-preview"><table><thead><tr><th>Linha</th><th>Descrição</th><th>Data</th><th>Tipo</th><th>Valor</th><th>Situação</th></tr></thead><tbody>{items.map(i=><tr key={i.line}><td>{i.line}</td><td>{i.transaction?.description||'—'}</td><td>{i.transaction?.date||'—'}</td><td>{i.transaction?.type==='income'?'Receita':i.transaction?.type==='expense'?'Despesa':'—'}</td><td>{i.transaction?.amount.toLocaleString('pt-BR',{style:'currency',currency:'BRL'})||'—'}</td><td className={i.error?'red':'green'}>{i.error||'Pronto'}</td></tr>)}</tbody></table></div>
   <div className="report-actions"><button className="primary" type="button" disabled={busy||!valid.length} onClick={()=>void importRecords()}>{busy?'Importando...':`Importar ${valid.length} lançamento(s)`}</button></div>
  </>}
  {busy&&<p role="status">Processando planilha...</p>}
  {feedback&&<p className="import-feedback" role="status">{feedback}</p>}
  {error&&<div className="alert" role="alert">{error}</div>}
 </section>;
}
