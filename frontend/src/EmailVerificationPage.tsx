import { useEffect, useState } from 'react';
import { Wallet } from 'lucide-react';

export function EmailVerificationPage({apiUrl,token,onComplete,onBack}: {
  apiUrl:string; token:string; onComplete:()=>void; onBack:()=>void;
}) {
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState('');
  useEffect(()=>{
    window.history.replaceState(null,'',window.location.pathname+window.location.search);
  },[]);
  const confirm=async()=>{
    setBusy(true);setError('');
    try {
      const response=await fetch(`${apiUrl}/api/auth/verify-email`,{
        method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({token})
      });
      if(!response.ok){
        const detail=await response.json().catch(()=>({}));
        throw new Error(detail.error||'Não foi possível confirmar agora. Tente novamente mais tarde.');
      }
      onComplete();
    } catch(e){setError(e instanceof Error?e.message:'Não foi possível confirmar seu e-mail.');}
    finally{setBusy(false);}
  };
  return <div className="login-wrap"><section className="login-card auth-recovery">
    <div className="logo login-logo"><Wallet size={22}/> Meu Financeiro</div>
    <h1>Confirmar e-mail</h1>
    <p>Confirme seu e-mail para acessar sua conta. O link é válido por 30 minutos.</p>
    {error&&<div className="alert" role="alert">{error}</div>}
    <button className="primary full" disabled={busy} onClick={()=>void confirm()}>{busy?'Confirmando...':'Confirmar meu e-mail'}</button>
    <div className="auth-links"><button className="auth-mode-switch" disabled={busy} onClick={onBack}>Voltar para entrar</button></div>
  </section></div>;
}
