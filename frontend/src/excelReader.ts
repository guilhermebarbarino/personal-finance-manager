// Offline XLSX reader for simple finance worksheets. No uploads to third parties.
export type Cell = string | number;
const maxFile = 5 * 1024 * 1024;
const decoder = new TextDecoder('utf-8');
const ns = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main';
function xml(input:string):Document {
 const parsed = new DOMParser().parseFromString(input,'application/xml');
 if(parsed.getElementsByTagName('parsererror').length) throw new Error('O Excel contém XML inválido.');
 return parsed;
}
function children(parent:Element,tag:string):Element[] {
 return Array.from(parent.getElementsByTagNameNS(ns,tag));
}
async function zipEntries(buffer:ArrayBuffer):Promise<Map<string,string>> {
 const view=new DataView(buffer), bytes=new Uint8Array(buffer), entries=new Map<string,string>();
 // Parse the central directory: Excel often uses data descriptors in local records.
 let end=-1;
 for(let i=bytes.length-22;i>=Math.max(0,bytes.length-65557);i--){
  if(view.getUint32(i,true)===0x06054b50){end=i;break;}
 }
 if(end<0)throw new Error('Arquivo Excel inválido ou incompleto.');
 const count=view.getUint16(end+10,true),dirStart=view.getUint32(end+16,true);
 if(count>1000)throw new Error('Planilha grande demais.');
 let p=dirStart;
 for(let i=0;i<count;i++){
  if(p+46>bytes.length||view.getUint32(p,true)!==0x02014b50)throw new Error('Estrutura do arquivo Excel inválida.');
  const method=view.getUint16(p+10,true),compressed=view.getUint32(p+20,true);
  const uncompressed=view.getUint32(p+24,true);
  const nameLength=view.getUint16(p+28,true),extraLength=view.getUint16(p+30,true),commentLength=view.getUint16(p+32,true);
  const name=decoder.decode(bytes.subarray(p+46,p+46+nameLength));
  const offset=view.getUint32(p+42,true);
  p+=46+nameLength+extraLength+commentLength;
  if(name!=='xl/sharedStrings.xml'&&!/^xl\\/worksheets\\/sheet\\d+\\.xml$/.test(name))continue;
  if(uncompressed>10*1024*1024||compressed>5*1024*1024)throw new Error('Planilha muito grande.');
  if(offset+30>bytes.length||view.getUint32(offset,true)!==0x04034b50)throw new Error('Arquivo Excel corrompido.');
  const start=offset+30+view.getUint16(offset+26,true)+view.getUint16(offset+28,true);
  if(start+compressed>bytes.length)throw new Error('Arquivo Excel incompleto.');
  const data=bytes.slice(start,start+compressed);
  let output:Uint8Array;
  if(method===0)output=data;
  else if(method===8){
   if(typeof DecompressionStream==='undefined')throw new Error('Navegador incompatível com este arquivo. Exporte como CSV.');
   const stream=new Blob([data]).stream().pipeThrough(new DecompressionStream('deflate-raw'));
   output=new Uint8Array(await new Response(stream).arrayBuffer());
  }else throw new Error('Compactação do Excel não suportada.');
  if(output.byteLength>10*1024*1024)throw new Error('Planilha muito grande.');
  entries.set(name,decoder.decode(output));
 }
 return entries;
}
function excelDate(n:number):string{
 const d=new Date(Date.UTC(1899,11,30)+Math.floor(n)*86400000);
 return d.toISOString().slice(0,10);
}
export async function parseXlsx(file:File):Promise<Cell[][]>{
 if(file.size>maxFile)throw new Error('O arquivo deve ter no máximo 5 MB.');
 const zipped=await zipEntries(await file.arrayBuffer());
 const sheet=[...zipped.keys()].filter(x=>/^xl\/worksheets\/sheet\d+\.xml$/.test(x)).sort((a,b)=>Number(a.match(/\d+/g)?.at(-1))-Number(b.match(/\d+/g)?.at(-1)))[0];
 if(!sheet)throw new Error('Nenhuma planilha encontrada no arquivo Excel.');
 const strings=zipped.has('xl/sharedStrings.xml')?children(xml(zipped.get('xl/sharedStrings.xml')!).documentElement,'si').map(si=>children(si,'t').map(t=>t.textContent||'').join('')):[];
 const rows:Cell[][]=[];
 for(const row of children(xml(zipped.get(sheet)!).documentElement,'row')){
  const values:Cell[]=[];
  for(const cell of children(row,'c')){
   const ref=cell.getAttribute('r')||'A1';const letters=ref.match(/^[A-Z]+/)?.[0]||'A';
   let index=0;for(const char of letters)index=index*26+char.charCodeAt(0)-64;index--;
   if(index>30)continue;
   const type=cell.getAttribute('t'), value=children(cell,'v')[0]?.textContent||'';
   let result:Cell=value;
   if(type==='s')result=strings[Number(value)]||'';
   else if(type==='inlineStr')result=children(cell,'t').map(t=>t.textContent||'').join('');
   else if(!type&&value!==''&&Number.isFinite(Number(value)))result=Number(value);
   // Excel dates are numeric: the import screen also accepts serial dates.
   values[index]=result;
  }
  if(values.some(v=>v!==undefined&&String(v).trim()!==''))rows.push(values);
 }
 return rows;
}
function parseDelimited(text:string,delimiter:string):string[][]{
 const rows:string[][]=[];let row:string[]=[],part='',quoted=false;
 for(let i=0;i<text.length;i++){const c=text[i];
  if(c==='"'){if(quoted&&text[i+1]==='"'){part+='"';i++;}else quoted=!quoted;}
  else if(c===delimiter&&!quoted){row.push(part);part='';}
  else if((c==='\n'||c==='\r')&&!quoted){if(c==='\r'&&text[i+1]==='\n')i++;row.push(part);if(row.some(v=>v.trim()))rows.push(row);row=[];part='';}
  else part+=c;
 }
 row.push(part);if(row.some(v=>v.trim()))rows.push(row);return rows;
}
export async function parseSheet(file:File):Promise<Cell[][]>{
 const ext=file.name.toLowerCase();
 if(ext.endsWith('.xlsx'))return parseXlsx(file);
 if(ext.endsWith('.csv')){if(file.size>maxFile)throw new Error('O arquivo deve ter no máximo 5 MB.');
  const content=(await file.text()).replace(/^\uFEFF/,'');
  const first=content.split(/\r?\n/,1)[0];
  return parseDelimited(content,(first.match(/;/g)||[]).length>=(first.match(/,/g)||[]).length?';':',');
 }
 throw new Error('Envie um arquivo .xlsx ou .csv exportado pelo Excel.');
}
