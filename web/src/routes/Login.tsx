import { useMutation } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { api } from '../lib/api'

export function Login() {
  const navigate = useNavigate()
  const [step, setStep] = useState<'password' | 'mfa'>('password')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [code, setCode] = useState('')
  const login = useMutation({ mutationFn: async () => {
    if (step === 'password') {
      await api('/api/auth/login/password', { method: 'POST', body: JSON.stringify({ email, password }) })
      setStep('mfa')
      return
    }
    await api('/api/auth/login/mfa', { method: 'POST', body: JSON.stringify({ authenticatorCode: code }) })
    await navigate({ to: '/' })
  } })
  return <section className="login-workspace"><div><p className="eyebrow">Restricted operations</p><h1>{step === 'password' ? 'Enter the control room.' : 'Confirm it is you.'}</h1><p>{step === 'password' ? 'Use the invited account assigned to your pilot role.' : 'Enter the six-digit code from your authenticator.'}</p></div><form onSubmit={(event) => { event.preventDefault(); login.mutate() }}>{step === 'password' ? <><label>Email<input type="email" autoComplete="username" required value={email} onChange={(event) => setEmail(event.target.value)} /></label><label>Password<input type="password" autoComplete="current-password" required value={password} onChange={(event) => setPassword(event.target.value)} /></label></> : <label>Authenticator code<input inputMode="numeric" autoComplete="one-time-code" pattern="[0-9 ]{6,8}" required value={code} onChange={(event) => setCode(event.target.value)} /></label>}{login.error && <p className="form-error" role="alert">Authentication failed. Check your credentials and try again.</p>}<button className="action action--primary" type="submit" disabled={login.isPending}>{login.isPending ? 'Checking…' : step === 'password' ? 'Continue securely' : 'Open operations'}</button></form></section>
}
