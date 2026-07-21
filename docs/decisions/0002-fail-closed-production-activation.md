# ADR 0002: Fail-closed production activation

Status: Accepted — 2026-07-21

Public WhatsApp inbound and outbound use the same executable gate. Opening it requires append-only evidence for every external and technical approval plus runtime profile and guidance checks. The latest revocation closes the path. No single configuration flag opens production.

Sandbox WhatsApp and Telegram remain explicitly controlled-test paths so engineering and partner rehearsal can continue without implying public approval.
