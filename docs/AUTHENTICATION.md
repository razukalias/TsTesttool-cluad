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
| `certificate` | only a client certificate (mutual TLS) | `CertificatePath`, `CertificatePassword`, `CertificateKeyPath` |

A certificate can be added to **any** type (the login call and the requests of the step are then sent with it). `.pfx`/`.p12` use `CertificatePassword`; for `.pem` set `CertificateKeyPath` too. Write the password as `${CERT_PASSWORD}`.

### OAuth2 grants
`GrantType`: `client_credentials` (default), `password` (`User` + `Password`) or `refresh_token` (`RefreshToken`). A browser sign-in (authorization code) cannot run unattended: sign in once, keep the refresh token as a pipeline secret (`"RefreshToken": "${REFRESH_TOKEN}"`) and use the `refresh_token` grant. When the server answers with a new refresh token it is used for the renewals; if the server refuses it, the configured grant is used again.

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

## A profile per environment
A profile called `api@qa` is used instead of `api` when the run is for the environment `qa` (`api@prod` for `prod`, …). Steps and the default keep writing `api`; an environment without its own profile uses `api`.

## A different user per data row
Use request columns in the profile: `"LoginBody": "{\"user\":\"_user_\",\"password\":\"_password_\"}"`. The columns `user` and `password` of the step's data row are filled in; every different value gets its own login and token.

## Secrets
Write `${NAME}` for passwords, secrets and keys (see `CI_CD.md`): the value comes from `--set NAME=value` or the environment variable `NAME` and is never saved in the workbook. Tokens and passwords are masked as `***` in the internal log, reports and history; the request log of a step shows which header was added (`authentication=api (Authorization added, value hidden)`). *Test login* in the window logs in once and says whether it worked.

## Not covered yet
The interactive OAuth2 authorization-code sign-in (use a refresh token, see above), PKCS#11 / Windows certificate store certificates (file certificates only).
