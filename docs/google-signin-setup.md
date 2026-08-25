# Google sign-in — setup

How Google sign-in is wired across LaundryGhar, and the console steps that must be
completed before it can work. Everything in the codebase is done; what remains is
credential configuration in Google Cloud, which only the project owner can do.

## How it works

```
client                         Identity (core, :5050)
──────                         ──────────────────────
Google consent screen
  → Google ID token (JWT)
      POST /customer/auth/google  { idToken }   ← customer app (web + Android + iOS)
      POST /auth/google           { idToken }   ← admin-web, pos-web
                               → fetch Google's JWKS via OIDC discovery (cached, auto-refreshed)
                               → verify signature, iss, aud, exp
                               → require email_verified
                               → customer: find-or-create by google `sub`, then email
                                 staff:    match an EXISTING user by email, never create
                               → LaundryGhar JWT + refresh token
```

The backend never receives a Google access token and never calls a Google API on the
user's behalf — it only verifies the ID token's signature. That is why **no client
secret is needed anywhere**, and why the Firebase Admin SDK service-account key is
not used by this flow at all.

Client libraries:

| Surface | Library |
|---|---|
| customer-mobile (web, Android, iOS) | `expo-auth-session/providers/google` — pure JS, works in Expo Go |
| admin-web, pos-web | Google Identity Services (`accounts.google.com/gsi/client`), loaded on demand |

## Required console steps

### 1. Enable the Google provider

Firebase console → **Authentication → Sign-in method → Google → Enable**.

This makes Google Cloud create the OAuth clients the next steps depend on.

### 2. Fix the mismatched app registrations

⚠️ The two config files currently in the repo were generated for apps registered under
identifiers that do not match this codebase, and neither has any OAuth client:

| File | Registered as | `app.config.ts` uses | Verdict |
|---|---|---|---|
| `customer-mobile/google-services.json` | `com.launddryghar.app` | `com.laundryghar.customer` | mismatch (note the double `d`) |
| `customer-mobile/GoogleService-Info.plist` | `com.laundrygahar.ios` | `com.laundryghar.customer` | mismatch (`gahar` / `ghar`) |

Both also have empty `oauth_client` / no `CLIENT_ID`, which is the direct cause of
Google sign-in not working: without an OAuth client there is nothing to sign in with.

In Firebase console → **Project settings → Your apps**, register (or re-register):

- **Android** — package name `com.laundryghar.customer`, plus the signing SHA-1
  (`eas credentials`, or `keytool -list -v -keystore ~/.android/debug.keystore` for
  local debug builds; add the release SHA-1 too before shipping)
- **iOS** — bundle ID `com.laundryghar.customer`
- **Web** — for admin-web, pos-web and the customer app's web build

Then download the fresh `google-services.json` / `GoogleService-Info.plist` and replace
the two files. They are only needed for FCM later, not for this sign-in flow — but
leaving stale ones in place will cause confusing failures when push notifications are wired up.

### 3. Authorize the web origins

Google Cloud console → **APIs & Services → Credentials → OAuth 2.0 Client IDs → Web client**.

Add every origin that will show a Google button, under **Authorized JavaScript origins**:

```
http://localhost:5173     # admin-web dev
http://localhost:5174     # pos-web dev
http://localhost:8081     # Expo web dev
https://<your admin domain>
https://<your pos domain>
```

Expo web's redirect also needs **Authorized redirect URIs** — run the app once and copy
the exact URI from the browser's error message, since it encodes the dev port.

### 4. Configure the client IDs

**Backend** (`core.WebApi`) — the accepted `aud` values. Every platform that signs a user
in must be listed or its logins fail with an audience mismatch:

```bash
GoogleAuth__WebClientId=<web client id>.apps.googleusercontent.com
GoogleAuth__AndroidClientId=<android client id>.apps.googleusercontent.com
GoogleAuth__IosClientId=<ios client id>.apps.googleusercontent.com
```

With none set, both Google endpoints fail closed with
"Google sign-in is not configured on this server" — deliberately, since an empty
audience list would otherwise accept a token from any Google project.

**customer-mobile** (`.env.local`, or EAS secrets for builds):

```bash
GOOGLE_WEB_CLIENT_ID=...
GOOGLE_ANDROID_CLIENT_ID=...
GOOGLE_IOS_CLIENT_ID=...
```

The Google button hides itself when no client ID exists for the running platform, so a
partially-configured environment shows no broken button.

**admin-web / pos-web** (`.env`):

```bash
VITE_GOOGLE_CLIENT_ID=<web client id>.apps.googleusercontent.com
```

## Staff vs customer behaviour

|  | Customer (`/customer/auth/google`) | Staff (`/auth/google`) |
|---|---|---|
| Unknown email | creates the account | **rejected** — "ask an administrator to invite you" |
| Unverified Google email | rejected | rejected |
| Account matching | google `sub`, then verified email | verified email only |
| Result | customer JWT + refresh | staff JWT + refresh + HttpOnly `lg_refresh` cookie |

Staff accounts are never self-provisioned: a staff identity carries roles, scope
memberships and permissions, so it must come from the invite flow.

## The service-account key

`laundry-ghar-5b39e-firebase-adminsdk-*.json` is **not used** by this flow and is now
gitignored (it was staged for commit — `git rm --cached` has undone that). Its private
key was pasted into a chat transcript, so treat it as compromised:

Firebase console → **Project settings → Service accounts → Manage service account
permissions → Keys → delete the key `0cec085ba7…` and generate a new one** if you need
one for other work. Store it outside the repo.

Note that Firebase *web* API keys (`AIzaSy…`), `google-services.json` and
`GoogleService-Info.plist` are **not** secrets — they are public client identifiers,
protected by package-name/SHA-1 and origin restrictions rather than secrecy.

## Testing OTP without SMS

Non-production environments accept a master OTP so testers and store reviewers can sign
in without SMS/WhatsApp delivery:

| Flow | Setting | Value | Digits |
|---|---|---|---|
| Customer login / phone link | `Otp__CustomerTestCode` | `1234` | 4 |
| Staff / rider / step-up | `Otp__TestCode` | `123456` | 6 |

Both are blocked twice over in Production: the verify path ignores them when
`IsProduction()`, and the host **refuses to start** if either is set there. Delete them
from an environment's configuration the moment real OTP delivery goes live.

Customer OTP length is `Otp__CustomerCodeLength` (default 4, clamped to 4–8). The mobile
app's `OTP_LENGTH` and the request validator both follow it, so changing it in one place
is enough — but the app constant must be updated to match.
