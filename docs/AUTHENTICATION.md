# Authentication

Steps log in through **profiles**; no login step is needed in the testcase. The runner logs in when the first step needs it, keeps the token, renews it before it expires and logs in again (once) when a request answers 401.

## Where
* **Instance** – right-click the instance in the editor tree › *Authentication…*, or *Authentication…* in the instance's Configuration panel. Save for *This instance only* or *All instances*.
* **Global** – profiles saved for all instances are at the top of `appsettings.json` (`"Auth"`); an instance profile with the same name hides the global one.
* **Step** – step settings › *Authentication*: `default` (the instance's default profile), `none` (no credentials), or a profile name. A header written in the step itself (for example `Authorization`) always wins.
* **Inline in one step** – `StepConfig`: `{"auth":{"type":"basic","user":"ann","password":"${ANN_PASSWORD}"}}`.

## Types
| type | what it does | main settings |
|---|---|---|
| `bearer` | login call returns a token, sent as `Authorization: Bearer <token>` | `LoginUrl`, `LoginBody`, `TokenPath` (`$.access_token`), `ExpiresInPath` or `TtlSeconds`, `Header`, `Prefix` |
| `oauth2` | client credentials; renewed before it expires | `TokenUrl`, `ClientId`, `ClientSecret`, `Scope`, `Audience`, `ClientAuth` (`body` or `basic`) |
| `basic` | `Authorization: Basic …` | `User`, `Password` |
| `apikey` | a key in a header or the query string | `Name`, `Value`, `In` (`header` or `query`) |
| `headers` | fixed headers | `Headers` |
| `cookie` | login call; the session cookie is kept for the next requests | `LoginUrl`, `LoginBody`, `TtlSeconds` |

## Example (appsettings.json)
```json
"Instances": {
  "Shop": {
    "Auth": {
      "Default": "api",
      "Profiles": {
        "api": { "Type": "bearer", "LoginUrl": "${BASE_URL}/login", "LoginBody": "{\"user\":\"${API_USER}\",\"password\":\"${API_PASSWORD}\"}" },
        "partner": { "Type": "oauth2", "TokenUrl": "${BASE_URL}/oauth/token", "ClientId": "${CLIENT_ID}", "ClientSecret": "${CLIENT_SECRET}", "Scope": "orders" }
      }
    }
  }
}
```
A step that needs the partner login writes `{"auth":"partner"}` in its settings; a public endpoint writes `{"auth":"none"}`.

## A different user per data row
Use request columns in the profile: `"LoginBody": "{\"user\":\"_user_\",\"password\":\"_password_\"}"`. The columns `user` and `password` of the step's data row are filled in; every different value gets its own login and token.

## Secrets
Write `${NAME}` for passwords, secrets and keys (see `CI_CD.md`): the value comes from `--set NAME=value` or the environment variable `NAME` and is never saved in the workbook. Tokens and passwords are masked as `***` in the internal log, reports and history; the request log of a step shows which header was added (`authentication=api (Authorization added, value hidden)`). *Test login* in the window logs in once and says whether it worked.

## Not covered yet
OAuth2 authorization-code / password grants and refresh tokens, certificates, per-environment profiles (use `${NAME}` values per run for now).
