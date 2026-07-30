# First10 access recovery

This runbook covers operator access only. Never share accounts, passwords, authenticator seeds, or
recovery codes. Perform recovery over a separately verified communication channel and record the
incident or service ticket identifier outside First10.

## Normal recovery-code sign-in

1. Verify the operator is using one of the ten codes saved during enrollment.
2. Complete the password step at `POST /api/auth/login/password`.
3. Submit one code to `POST /api/auth/login/recovery`. A successful submission consumes the code.
4. Ask the operator to confirm normal access and retain the remaining codes offline.

Never copy a recovery code into a ticket, chat, audit field, log query, or screenshot. A reused code
must fail. Repeated failure is a security signal and should be escalated.

## Administrator-assisted MFA reset

Use this path only when the operator has lost both the authenticator and all recovery codes.

1. Verify the operator's identity using the approved offline pilot contact list. Record who performed
   the verification and the external ticket identifier.
2. A different Administrator signs in with password and TOTP immediately before recovery. The API
   accepts assisted recovery only within five minutes of that reauthentication.
3. The Administrator submits the target user ID to `POST /api/auth/mfa-recovery` and passes the
   returned one-time token directly to the verified operator. The token expires after 30 minutes.
4. First10 immediately revokes the target's sessions, disables the prior authenticator, and
   invalidates all prior recovery codes.
5. The operator fetches a new authenticator seed through `POST /api/auth/invitations/enrollment`,
   confirms a TOTP through `POST /api/auth/invitations/accept`, and stores the new recovery codes.
   The operator's password is not changed by this flow.
6. Confirm the audit chain contains `identity.mfa_recovery.started` and
   `identity.mfa_recovery.completed`, with the Administrator and target IDs but no token, seed, or
   recovery code.

An Administrator cannot perform assisted recovery on their own account. If no independent
Administrator is available, use the break-glass procedure below.

## Break glass

1. Obtain approval from the named pilot incident commander and one additional named approver.
2. Restore access through a controlled database/identity administration session using the deployment
   platform's audited privileged-access mechanism. Do not add a shared or permanent bypass account.
3. Revoke the affected user's sessions and MFA material, then complete the normal assisted reset with
   a different named Administrator as soon as possible.
4. Alert the pilot security contact, preserve the platform access record, verify the First10 audit
   chain, and complete a post-incident review before closing the event.

The initial deployment bootstrap secret is not a recovery mechanism. It can create only the first
Administrator invitation and is permanently ineffective once any user exists.

## Secret and key handling

- Keep bootstrap, webhook, provider, contact-encryption, and data-protection material in the selected
  environment's managed secret/key facilities; never in source-controlled settings.
- Use different material per environment and per purpose. Version rotatable credentials and keep an
  explicitly bounded overlap only where an integration requires it.
- Persist ASP.NET Core data-protection keys outside the container in production and protect that key
  repository with the deployment platform's key service. Back up the key repository for at least as
  long as protected data must remain readable.
- After a rotation, verify new work uses the active version, already accepted work remains readable,
  retired credentials fail after the overlap, and the audit/operations event contains version IDs
  only—not either secret.

## Verification checklist

- The affected user's pre-reset cookie receives `403` immediately.
- An old recovery code fails and a new one succeeds only once.
- The one-time enrollment token cannot be replayed.
- Dispatcher, Administrator, and ClinicalApprover role boundaries remain unchanged.
- Application logs, traces, and audit payloads contain no password, TOTP seed/code, recovery code,
  cookie, invitation token, or reporter identifier.
