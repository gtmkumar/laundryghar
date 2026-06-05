# Laundry Ghar — Rider Mobile App

React Native / Expo SDK 52 app for riders: today's assignments, status updates, GPS pinging, and profile.

---

## Stack

| Layer          | Choice                                               |
|---------------|------------------------------------------------------|
| Framework      | Expo SDK 52 (managed workflow)                       |
| Language       | TypeScript (strict)                                  |
| Router         | expo-router v4 (file-based, typed routes)            |
| State          | Zustand 5 (auth store) + TanStack Query 5 (server)   |
| Styling        | NativeWind 4 (Tailwind CSS for React Native)         |
| HTTP client    | axios (one instance per service, refresh-coalesced)  |
| Token storage  | expo-secure-store                                    |
| GPS            | expo-location                                        |

---

## Setup

### Prerequisites

- Node 22
- npm (or yarn)
- Expo CLI: `npm install -g expo-cli` (or use `npx expo`)
- For native builds: Xcode 15+ (iOS) / Android Studio (Android)

### Install

```bash
cd rider-mobile
npm install
```

### Configure service URLs

The app reads service base URLs from `app.config.ts` → `extra`. In development you can override them with environment variables:

```bash
IDENTITY_API_URL=http://localhost:5000 \
LOGISTICS_API_URL=http://localhost:5004 \
npx expo start
```

Defaults:

| Service  | Default URL             |
|----------|------------------------|
| Identity | http://localhost:5000  |
| Logistics | http://localhost:5004 |

For production, set these as EAS secrets and reference them in `eas.json`.

### Run

```bash
# Expo Go (JS only — no native modules beyond managed set)
npx expo start

# iOS simulator
npx expo run:ios

# Android emulator/device
npx expo run:android
```

### Typecheck

```bash
npm run typecheck
# or
npx tsc --noEmit
```

---

## Architecture

```
rider-mobile/
  app/
    _layout.tsx              # Root: query client, safe area, auth bootstrap
    index.tsx                # Entry redirect
    (auth)/
      _layout.tsx
      login.tsx              # Password login screen
    (app)/
      _layout.tsx            # Auth guard
      (tabs)/
        _layout.tsx          # Tab bar (Assignments / Location / Profile)
        assignments.tsx      # Today's shifts list
        location.tsx         # GPS ping screen
        profile.tsx          # Rider profile + logout
      assignments/
        [id].tsx             # Assignment detail + status update
  src/
    api/
      auth.ts                # POST /auth/password/login, /auth/refresh, /auth/logout
      rider.ts               # GET /rider/me, /rider/assignments/today, PATCH status, POST ping
      client.ts              # axios factory, configureApiAuth, unwrap helpers, ApiError
    constants/
      config.ts              # Read from expo-constants extra (service URLs)
    hooks/
      useRider.ts            # TanStack Query hooks wrapping rider.ts
    store/
      authStore.ts           # Zustand store: tokens + rider profile + bootstrapApiAuth()
    types/
      api.ts                 # All TS types matching backend contracts
    components/
      ui/
        Button.tsx
        TextInput.tsx
        ScreenLoader.tsx
        ErrorState.tsx
        EmptyState.tsx
```

---

## Rider Authentication

Riders are **system users** (`user_type='rider'`) — they do **not** use OTP. They log in with a password via the shared Identity auth endpoint:

```
POST {Identity}/api/v1/auth/password/login
Body: { "identifier": "...", "password": "..." }
```

The `identifier` can be:
- Phone number (E.164)
- Email address
- Rider code (e.g. `RDR-0001`)

On success the backend returns `{ status: true, data: { accessToken, refreshToken } }`. The access token is a system JWT containing:
- `sub` = user id (UUID)
- `user_type` = `rider`
- `brand_id` = the brand the rider belongs to

Both tokens are persisted in `expo-secure-store` and the access token is attached as `Authorization: Bearer <token>` on every subsequent request. A 401 response triggers one coalesced refresh attempt before logging the rider out.

### Seeding a rider (development)

1. **Create the user** via Identity admin API (`POST /api/v1/admin/users`) with `userType = "rider"` and set a password.
2. **Create the rider profile** via Logistics admin API (`POST /api/v1/admin/riders`) supplying `userId`, `franchiseId`, and capacity fields.
3. The rider can now log in with the phone/email/password set in step 1.

---

## API Contracts

All endpoints use the envelope `{ status: boolean, data?: T, message?: { responseMessage, errorTypeCode, errorMessage } }`.

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `{Identity}/api/v1/auth/password/login` | Rider login |
| POST | `{Identity}/api/v1/auth/refresh` | Token refresh |
| POST | `{Identity}/api/v1/auth/logout` | Logout |
| GET  | `{Logistics}/api/v1/rider/me` | Rider profile |
| GET  | `{Logistics}/api/v1/rider/assignments/today` | Today's assignments |
| PATCH | `{Logistics}/api/v1/rider/assignments/{id}/status` | Update own assignment status |
| POST | `{Logistics}/api/v1/rider/location/ping` | Batch GPS ping |

---

## GPS / Location

The current implementation uses **foreground one-shot pings** (`expo-location.getCurrentPositionAsync`). A rider taps "Send GPS Ping" on the Location tab to broadcast their position.

Background continuous tracking (required for production shift tracking) needs:
- `expo-task-manager` + `expo-location` background task
- A custom native build (not Expo Go compatible)
- `ACCESS_BACKGROUND_LOCATION` permission on Android

This is planned as a follow-up slice.
