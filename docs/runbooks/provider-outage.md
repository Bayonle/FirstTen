# Provider outage

1. Identify the degraded dependency: Telegram, Meta, OpenAI, object storage, or PostgreSQL.
2. Keep accepted work durable. Do not purge queues or blindly replay messages with an unknown provider outcome.
3. OpenAI degradation must enter the 30-second manual path; object-storage/redaction degradation continues text/audio-only where the privacy policy permits.
4. Unknown outbound acceptance is terminal for automatic retry. A dispatcher may inspect the intent before a deliberate retry.
5. Public WhatsApp stays closed if its activation evidence or credentials drift.
6. Record start/end time, affected cohort, queue age, deadline misses, actions, and reconciliation result—never raw contact, transcript, token, or media content.
