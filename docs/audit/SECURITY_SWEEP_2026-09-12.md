# Security sweep — 2026-09-12

Scope: the live API (`api.riseoftheancients.com`), the game page (`play.riseoftheancients.com`),
the WebGL client, and the codebase behind them. Everything below that was found wanting is fixed in
the commits named; nothing in this note is still live.

## Fixed

| # | Where | What | Fix |
|---|---|---|---|
| 1 | play. (Caddy) | No security headers at all: the game page could be framed, a response sniffed into HTML, an injected script could reach any host. | HSTS, `nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`, `Permissions-Policy`, and a CSP pinned to the origin and the API. `'wasm-unsafe-eval'` is all Unity needs — the build boots under it with no violation. `81b060c`, `BETA_DEPLOY.md` §7a. |
| 2 | play. (Caddy) | 41 MB of wasm gzipped on the fly, per player, on one CPU; no `Content-Length` for the Unity loader. | `.gz`/`.zst` twins made at deploy (`tools/deploy-webgl.ps1`), `file_server precompressed`. |
| 3 | API | `Server: Kestrel` on every response. | `AddServerHeader = false`. `81b060c` |
| 4 | API | 30 MB request bodies accepted and parsed before validation refused them. | 64 KB cap; a declared length over it is a plain 413 before anything reads the body. `4 KB` is the largest field any validator takes. |
| 5 | API | `TrustedProxies` took only a bare address, so a recreated Caddy on a new address left forwarded headers silently ignored (rate-limit buckets and audit IPs collapsed onto the proxy). | CIDR entries → `KnownIPNetworks`; the override names `172.18.0.0/16`. RF1, `070c28a`, four unit tests. Verified live: the anon rate-limit key carries the real client IP. |
| 6 | Client | UI Toolkit labels parse rich-text tags by default, so `<size=300>` in chat or a guild description rendered as such for everyone. | `enableRichText = false` on chat bodies, private messages, guild description and MOTD. `accdb17` |
| 7 | API | `/icons/body` cached a day, so a player who saw the placeholder figure kept it a day after the real one shipped. | `no-cache` (revalidate), like the atlas. `8377ed6` |
| 8 | API | HSTS was the framework's 30-day default. | A year, like the game page. |

## New standing test

`EndpointAuthorizationSweepTests` walks the live action table: every route is protected or on a
named anonymous list (`register`, `login`, `refresh`, the two password-reset routes, `legal/terms`,
`legal/privacy`); every route under `api/admin`, `api/dev`, `api/moderation` names an admin-grade
policy; then real requests — no token → 401 on every protected route, a signed-in ordinary player →
403 on every privileged one, the anonymous routes do not demand a token. Proven by neutering: with
`[Authorize]` commented off `SubjectsController` the sweep fails naming `api/subjects`.

## Checked and found sound

- API headers: CSP `default-src 'none'`, HSTS, nosniff, DENY, referrer `no-referrer`, permissions
  policy, `Cache-Control: no-store` (`SecurityHeadersMiddleware`, pinned by tests).
- CORS: only `https://play.riseoftheancients.com`; a foreign origin gets no `Access-Control-*`.
- TLS: 1.2+ only (TLS 1.1 refused). HTTP → HTTPS 308.
- Swagger not served outside Development (404 live).
- JWT: RS256, the token with `alg: none` is refused (401); the chat hub's negotiate demands a token.
- Errors: malformed JSON → RFC 9110 problem details with a trace id, no stack.
- Registration: username `[a-zA-Z0-9_-]{3,32}` and a reserved list; display name
  `[A-Za-z0-9_ -]`; password 8–128 with upper/lower/digit; email ≤ 255; terms version pinned;
  duplicates checked before a beta key is burned.
- Object ownership at the service layer: raid loot (participant only), share (summoner only),
  personal raids invisible to others by id, market cancel (seller only), friend accept (addressee
  only), equip (owned quantity), magic removal (summoner or world), private conversations scoped to
  the two parties, guild officer actions gated in `GuildService`.
- Chat: bodies trimmed and capped at 500, hub sends throttled per player, raid chat gated on a
  participation row, guild chat on membership.
- Rate limiting keys on the *verified* identity; anonymous traffic is per-IP; Redis outage fails
  open (a protection layer, not a dependency) and each breach is audited once per window.
- Ban gate runs on every mutating request, ahead of the 15-minute access-token lifetime.

## Left as is, with reasons

- `script-src 'unsafe-inline'` on the game page: the Unity template's boot script is inline. A
  nonce would need `apply-webgl-bg.ps1` to stamp one per deploy and Caddy to serve it; low value
  while nothing else on the page runs script.
- `baby.riseoftheancients.com` shares the Caddy but is not part of this system and was not reviewed.
