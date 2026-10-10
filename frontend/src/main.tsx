import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { Wallet, Plus, LogOut, TrendingUp, TrendingDown, Scale, Pencil, Trash2, X, Menu, Eye, EyeOff } from 'lucide-react';
import './style.css';
import { AnalyticsPage } from './AnalyticsPage';
import { ReportsPage } from './ReportsPage';
import { ImportPage } from './ImportPage';
import { readSession, writeSession, clearSession } from './authSession';
type Transaction = {id:string;description:string;amount:number;date:string;type:'income'|'expense';category:string;isPaid:boolean|null};
type Entry = Omit<Transaction,'id'>;
type Month = {month:number;income:number;expenses:number;balance:number};
type Dashboard={year:number;income:number;expenses:number;balance:number;months:Month[]};
const API = (import.meta.env.VITE_API_URL || 'http://localhost:8080').replace(/\/$/,'');
const brl = (n:number) => new Intl.NumberFormat('pt-BR',{style:'currency',currency:'BRL'}).format(n);
const months=['Jan','Fev','Mar','Abr','Mai','Jun','Jul','Ago','Set','Out','Nov','Dez'];
const today=()=>{const d=new Date();return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`};
const initial=():Entry=>({description:'',amount:0,date:today(),type:'expense',category:'Outros',isPaid:false});
function App(){
 const [token,setToken]=useState<string>(readSession); const [email,setEmail]=useState(''); const [password,setPassword]=useState('');
 const [displayName,setDisplayName]=useState(''); const [profileName,setProfileName]=useState('');
 const [showPassword,setShowPassword]=useState(false);
 const [resetToken,setResetToken]=useState(()=>new URLSearchParams(window.location.hash.replace(/^#/,'' )).get('reset-token')||'');
 const [forgotMode,setForgotMode]=useState(false); const [resetSent,setResetSent]=useState(false);
 const [registerMode,setRegisterMode]=useState(false); const [authNotice,setAuthNotice]=useState('');
 const [month,setMonth]=useState(today().slice(0,7)); const [rows,setRows]=useState<Transaction[]>([]);
 const [dash,setDash]=useState<Dashboard|null>(null); const [busy,setBusy]=useState(false); const [error,setError]=useState('');
 const [page,setPage]=useState<'overview'|'analytics'|'transactions'|'reports'|'import'>('overview');
 const [menuOpen,setMenuOpen]=useState(false);
 const [editing,setEditing]=useState<Transaction|null>(null); const [form,setForm]=useState<Entry|null>(null);
 const api=async(path:string,init:RequestInit={})=>{
  const response=await fetch(API+path,{...init,headers:{'Content-Type':'application/json',Authorization:`Bearer ${token}`,...init.headers}});
  if(response.status===401){clearSession();setToken('');throw new Error('Sua sessão expirou. Faça login novamente.');}
  if(!response.ok){const problem=await response.json().catch(()=>({}));throw new Error(problem.error || `Não foi possível concluir a operação (${response.status}).`);}
  return response.status===204?null:response.json();
 };
 const load=async()=>{
  setBusy(true);setError('');try{
   const [list,summary]=await Promise.all([api(`/api/transactions?month=${month}`),api(`/api/dashboard?year=${month.slice(0,4)}`)]);
   setRows(list);setDash(summary);
  }catch(e){setError((e as Error).message);}finally{setBusy(false);}
 };
 useEffect(()=>{if(token)void load()},[token,month]);
 useEffect(()=>{if(!token){setProfileName('');return;} let active=true;
  void api('/api/me').then((profile:unknown)=>{if(active)setProfileName((profile as {displayName?:string|null}).displayName||'');}).catch(()=>{});
  return ()=>{active=false;};
 },[token]);
 const login=async(e:React.FormEvent)=>{e.preventDefault();setBusy(true);setError('');try{
  const r=await fetch(`${API}/api/auth/login`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({email,password})});
  if(!r.ok)throw new Error('Email ou senha inválidos.');const data=await r.json();writeSession(data.token,data.expiresAt);setToken(data.token);setPassword('');
 }catch(e){setError((e as Error).message);}finally{setBusy(false);}};
 const register=async(e:React.FormEvent)=>{e.preventDefault();setBusy(true);setError('');setAuthNotice('');try{
  const r=await fetch(`${API}/api/auth/register`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({email,password,displayName})});
  if(!r.ok){const detail=await r.json().catch(()=>({}));throw new Error(detail.error||'Não foi possível criar sua conta. Tente novamente.');}
  setPassword('');setDisplayName('');setRegisterMode(false);setAuthNotice('Conta criada com sucesso. Faça login para continuar.');
 }catch(e){setError((e as Error).message);}finally{setBusy(false);}};
 const forgotPassword=async(e:React.FormEvent)=>{e.preventDefault();setBusy(true);setError('');setAuthNotice('');try{
  const response=await fetch(`${API}/api/auth/forgot-password`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({email})});
  if(!response.ok)throw new Error('Não foi possível solicitar a recuperação agora.');
  setResetSent(true);
 }catch(e){setError((e as Error).message);}finally{setBusy(false);}};
 const resetPassword=async(e:React.FormEvent)=>{e.preventDefault();setBusy(true);setError('');try{
  const response=await fetch(`${API}/api/auth/reset-password`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({token:resetToken,newPassword:password})});
  if(!response.ok){const detail=await response.json().catch(()=>({}));throw new Error(detail.error||'Link inválido ou expirado.');}
  window.history.replaceState(null,'',window.location.pathname+window.location.search);
  setResetToken('');setPassword('');setRegisterMode(false);setForgotMode(false);setAuthNotice('Senha redefinida. Entre com sua nova senha.');
 }catch(e){setError((e as Error).message);}finally{setBusy(false);}};
 const editProfile=async()=>{const name=window.prompt('Como você gostaria de ser chamado?',profileName);
  if(name===null)return;
  const value=name.trim();if(!value||value.length>100){setError('Informe um nome entre 1 e 100 caracteres.');return;}
  setError('');try{await api('/api/me',{method:'PUT',body:JSON.stringify({displayName:value})});setProfileName(value);}
  catch(e){setError((e as Error).message);}
 };
 const save=async(e:React.FormEvent)=>{e.preventDefault();if(!form)return;setBusy(true);setError('');try{
  await api(editing?`/api/transactions/${editing.id}`:'/api/transactions',{method:editing?'PUT':'POST',body:JSON.stringify(form)});
  setForm(null);setEditing(null);await load();
 }catch(e){setError((e as Error).message);}finally{setBusy(false);}};
 const remove=async(row:Transaction)=>{if(!window.confirm(`Excluir "${row.description}"?`))return;setBusy(true);setError('');try{await api(`/api/transactions/${row.id}`,{method:'DELETE'});await load();}catch(e){setError((e as Error).message);}finally{setBusy(false);}};
 const currentIncome=rows.filter(x=>x.type==='income').reduce((a,x)=>a+x.amount,0);
 const currentExpense=rows.filter(x=>x.type==='expense').reduce((a,x)=>a+x.amount,0);
 const series=dash?.months.map(m=>({...m,name:months[m.month-1]}))||[];
 if(!token)return <div className="login-wrap"><form className="login-card" onSubmit={resetToken?resetPassword:forgotMode?forgotPassword:registerMode?register:login}>
  <div className="logo login-logo"><Wallet size={22}/> Meu Financeiro</div>
  <h1>{resetToken?'Redefinir senha':forgotMode?'Recuperar senha':registerMode?'Criar conta':'Acesse sua conta'}</h1>
  <p>{resetToken?'Defina uma nova senha para sua conta.':forgotMode?'Informe seu e-mail para receber o link de recuperação.':registerMode?'Cadastre uma conta para ter seu próprio painel financeiro.':'Entre com seu e-mail e senha para gerenciar suas finanças.'}</p>
  {registerMode&&!forgotMode&&!resetToken&&<label>Como gostaria de ser chamado?<input type="text" autoComplete="given-name" maxLength={100} value={displayName} onChange={e=>setDisplayName(e.target.value)} placeholder="Seu nome" required/></label>}
  {!resetToken&&<label>Email<input type="email" autoComplete="username" value={email} onChange={e=>setEmail(e.target.value)} placeholder="seu@email.com" required/></label>}
  {!forgotMode&&<label>{resetToken?'Nova senha':'Senha'}<span className="auth-password-field"><input type={showPassword?'text':'password'} autoComplete={resetToken||registerMode?'new-password':'current-password'} maxLength={128} minLength={resetToken||registerMode?12:undefined} value={password} onChange={e=>setPassword(e.target.value)} placeholder={resetToken?'Nova senha':'Sua senha'} required/><button type="button" aria-label={showPassword?'Ocultar senha':'Mostrar senha'} onClick={()=>setShowPassword(v=>!v)}>{showPassword?<EyeOff size={19}/>:<Eye size={19}/>}</button></span></label>}
  {(registerMode||resetToken)&&<div className="login-password-note">Use uma senha com pelo menos 12 caracteres.</div>}
  {authNotice&&<div className="auth-notice" role="status">{authNotice}</div>}
  {error&&<div className="alert" role="alert">{error}</div>}
  {resetSent&&forgotMode&&<div className="auth-notice" role="status">Se a conta existir, você receberá um e-mail com o link de recuperação.</div>}
  <button className="primary full" disabled={busy}>{busy?'Aguarde...':resetToken?'Salvar nova senha':forgotMode?'Enviar link':registerMode?'Criar conta':'Entrar'}</button>
  {!registerMode&&!forgotMode&&!resetToken&&<button type="button" className="login-forgot-link" onClick={()=>{setForgotMode(true);setResetSent(false);setError('');}}>Esqueci minha senha</button>}
  <button type="button" className="auth-mode-switch" onClick={()=>{if(resetToken){window.history.replaceState(null,'',window.location.pathname+window.location.search);setResetToken('');}setRegisterMode(forgotMode||resetToken?false:!registerMode);setForgotMode(false);setResetSent(false);setShowPassword(false);setError('');setAuthNotice('');setPassword('');}}>{registerMode||forgotMode||resetToken?'Voltar para entrar':'Criar uma nova conta'}</button>
 </form></div>;
 return <div className="app"><aside className="sidebar"><div className="sidebar-header"><div className="logo"><Wallet size={23}/> Meu Financeiro</div><button type="button" className="mobile-menu-toggle" aria-label={menuOpen?'Fechar menu':'Abrir menu'} aria-controls="finance-navigation" aria-expanded={menuOpen} onClick={()=>setMenuOpen(open=>!open)}>{menuOpen?<X size={24}/>:<Menu size={24}/>}</button></div><nav id="finance-navigation" className={menuOpen?'mobile-open':''} aria-label="Navegação principal">{([['overview','▦ Visão geral'],['analytics','◫ Análises'],['transactions','▤ Lançamentos'],['reports','▤ Relatórios'],['import','▤ Importar Excel']] as const).map(([value,label])=><button key={value} type="button" className={page===value?'nav-active':''} aria-current={page===value?'page':undefined} onClick={()=>{setPage(value);setMenuOpen(false);}}>{label}</button>)}</nav><div className="side-foot">Controle financeiro pessoal<br/><small>Ambiente privado</small></div></aside><main className="main" id="main-content"><header className="top"><span className="eyebrow">PAINEL FINANCEIRO</span><button className="ghost" onClick={()=>{clearSession();setToken('');setDash(null);setRows([]);}}><LogOut size={17}/> Sair</button></header><div className="heading"><div><h1>{page==='overview'?'Visão geral':page==='analytics'?'Análises financeiras':page==='reports'?'Relatórios':page==='import'?'Importar Excel':'Lançamentos'}</h1><p>{page==='overview'?'Resumo financeiro do período selecionado.':page==='analytics'?'Explore categorias, comparativos e seu saldo acumulado.':page==='reports'?'Exporte receitas e despesas por mês ou por ano.':page==='import'?'Importe seus lançamentos em lote sem redigitar.':'Gerencie suas receitas e despesas em um espaço dedicado.'}</p></div><div className="controls"><input aria-label="Mês de referência" type="month" value={month} min="2000-01" max="2100-12" onChange={e=>setMonth(e.target.value)}/><button className="primary" onClick={()=>{setEditing(null);setForm(initial());}}><Plus size={18}/> Novo lançamento</button></div></div>{error&&<div className="alert">{error}</div>}{page==='overview'&&<><div className="welcome-header"><div><h2>Olá{profileName?', '+profileName.split(' ')[0]:''}!</h2><p>Confira como estão suas finanças.</p></div><button type="button" className="secondary" onClick={()=>void editProfile()}>{profileName?'Editar nome':'Definir meu nome'}</button></div><div className="section-kicker">RESUMO DO MÊS</div><div className="kpis"><div className="kpi"><div className="kpi-title"><span>Receitas do mês</span><TrendingUp size={20} className="green"/></div><strong>{brl(currentIncome)}</strong><small>Entradas no período selecionado</small></div><div className="kpi"><div className="kpi-title"><span>Despesas do mês</span><TrendingDown size={20} className="red"/></div><strong>{brl(currentExpense)}</strong><small>Saídas no período selecionado</small></div><div className="kpi"><div className="kpi-title"><span>Saldo do mês</span><Scale size={20} className="blue"/></div><strong className={currentIncome-currentExpense < 0 ? 'red' : 'blue'}>{brl(currentIncome-currentExpense)}</strong><small>Receitas menos despesas</small></div></div><section className="panel overview-next"><div className="panel-heading"><div><h2>Explore suas finanças</h2><p>Acesse relatórios detalhados ou gerencie seus lançamentos.</p></div></div><div className="overview-links"><button className="overview-link" onClick={()=>setPage('analytics')}><TrendingUp size={22}/><strong>Análises financeiras</strong><small>Comparativos, categorias e evolução do saldo</small></button><button className="overview-link" onClick={()=>setPage('transactions')}><Wallet size={22}/><strong>Meus lançamentos</strong><small>Consultar, cadastrar, editar e excluir</small></button></div></section></>}
{page==='analytics'&&<AnalyticsPage rows={rows} series={series} year={Number(month.slice(0,4))} selectedMonth={month} />}
{page==='import'&&<ImportPage api={api} onImported={load} />}
{page==='reports'&&<ReportsPage selectedMonth={month} api={api} />}
{page==='transactions'&&<section className="panel entries"><div className="panel-heading"><div><h2>Lançamentos</h2><p>Histórico de receitas e despesas em {month.split('-').reverse().join('/')}</p></div><span>{rows.length} registros</span></div><div className="table-wrap"><table><thead><tr><th>DESCRIÇÃO</th><th>CATEGORIA</th><th>DATA</th><th>TIPO</th><th>PAGAMENTO</th><th>VALOR</th><th>AÇÕES</th></tr></thead><tbody>{rows.length===0?<tr><td colSpan={7} className="empty">{busy?'Carregando...':'Nenhum lançamento neste mês. Comece adicionando um.'}</td></tr>:rows.map(t=><tr key={t.id}><td className="description" data-label="Descrição">{t.description}</td><td data-label="Categoria">{t.category}</td><td data-label="Data">{t.date.split('-').reverse().join('/')}</td><td data-label="Tipo"><span className={`pill ${t.type}`}>{t.type==='income'?'Receita':'Despesa'}</span></td><td data-label="Pagamento">{t.type==='income'?'—':<span className={`payment-status ${t.isPaid===true?'paid':t.isPaid===false?'pending':'unknown'}`}>{t.isPaid===true?'Paga':t.isPaid===false?'Não paga':'Não informado'}</span>}</td><td data-label="Valor" className={t.type==='income'?'green':'red'}>{t.type==='income'?'+':'−'} {brl(t.amount)}</td><td data-label="Ações"><div className="actions"><button aria-label={`Editar ${t.description}`} onClick={()=>{setEditing(t);setForm({description:t.description,amount:t.amount,date:t.date,type:t.type,category:t.category,isPaid:t.isPaid});}}><Pencil size={16}/></button><button aria-label={`Excluir ${t.description}`} onClick={()=>void remove(t)}><Trash2 size={16}/></button></div></td></tr>)}</tbody></table></div></section>}
</main>{form&&<div className="modal-back" onMouseDown={e=>{if(e.target===e.currentTarget){setForm(null);setEditing(null);}}}><form className="modal" onSubmit={save} role="dialog" aria-modal="true" aria-label={editing?"Editar lançamento":"Novo lançamento"}><div className="modal-head"><h2>{editing?'Editar lançamento':'Novo lançamento'}</h2><button type="button" className="icon-btn" aria-label="Fechar" onClick={()=>{setForm(null);setEditing(null);}}><X size={21}/></button></div><div className="field"><label>Descrição</label><input maxLength={150} required value={form.description} onChange={e=>setForm({...form,description:e.target.value})} placeholder="Ex.: Supermercado"/></div><div className="two"><div className="field"><label>Tipo</label><select value={form.type} onChange={e=>setForm({...form,type:e.target.value as Entry['type'],isPaid:e.target.value==='income'?null:(form.type==='income'?false:form.isPaid)})}><option value="expense">Despesa</option><option value="income">Receita</option></select></div><div className="field"><label>Valor (R$)</label><input type="number" step="0.01" min="0.01" max="999999999999.99" required value={form.amount||''} onChange={e=>setForm({...form,amount:Number(e.target.value)})}/></div></div><div className="two"><div className="field"><label>Data</label><input type="date" required value={form.date} onChange={e=>setForm({...form,date:e.target.value})}/></div><div className="field"><label>Categoria</label><input maxLength={60} required value={form.category} onChange={e=>setForm({...form,category:e.target.value})}/></div></div>{form.type==='expense'&&<div className="field"><label htmlFor="expense-payment">Situação do pagamento</label><select id="expense-payment" value={form.isPaid===null?'unknown':form.isPaid?'paid':'pending'} onChange={e=>setForm({...form,isPaid:e.target.value==='unknown'?null:e.target.value==='paid'})}><option value="pending">Não paga</option><option value="paid">Paga</option>{form.isPaid===null&&<option value="unknown">Não informado (lançamento anterior)</option>}</select></div>}<div className="modal-actions"><button type="button" className="secondary" onClick={()=>{setForm(null);setEditing(null);}}>Cancelar</button><button disabled={busy} className="primary">Salvar lançamento</button></div></form></div>}</div>;
}

createRoot(document.getElementById('root')!).render(<React.StrictMode><App/></React.StrictMode>);
