# Telegram controlled-test intake

Telegram is a temporary controlled test channel, not the public pilot channel. Tell every test
participant not to submit a real crash, victim identity, or unnecessary personal information.

## Configure locally

Create the bot through BotFather, then store its token and an independent webhook secret through
the AppHost user-secrets commands in the repository README. Never place either value in a URL,
source-controlled file, terminal recording, ticket, or chat message.

Expose the local API through an approved HTTPS development tunnel, then configure Telegram's
`setWebhook` request with:

- URL: `https://<test-host>/webhooks/telegram`
- `secret_token`: the same value stored as `Parameters:telegram-webhook-secret`
- `allowed_updates`: `["message"]`
- `drop_pending_updates`: `true` when resetting a controlled test run

Telegram will send the secret in `X-Telegram-Bot-Api-Secret-Token`. First10 rejects a missing or
incorrect header before parsing JSON. Rotate by configuring the new active secret and a previous
secret with a short `PreviousWebhookSecretValidUntilUtc` overlap; remove the previous value after
the window and verify it is rejected.

## Exercise the state machine

Use a private bot chat and run each sequence separately:

1. Voice, photo, location pin.
2. Photo, voice, location pin.
3. Voice only; verify the initial location request, one reminder after 30 seconds, and visible
   manual-review expiry.
4. Replay the same signed fixture; verify one receipt, one guided input, and one prompt set.
5. Send unsupported content; verify an open Intake recovery item and no hidden report.

Provider acceptance means only that Telegram accepted the send request. It is not evidence that a
person received or read the message.

## Safety checks

- Database rows contain provider media handles only, never raw image/audio bytes.
- Downstream envelopes contain an opaque contact reference and keyed reporter pseudonym, never the
  chat ID.
- Logs and traces contain no bot token, webhook secret, chat ID, message body, or media bytes.
- Stop the HTTPS tunnel and delete pending test updates after the exercise.
