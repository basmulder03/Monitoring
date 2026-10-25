# Security policy

Security fixes go into the latest 1.x release; there is no backport to older versions.

## Reporting a vulnerability

Please do not open a public issue for a security problem. Use GitHub's private vulnerability reporting for this repository (the "Report a vulnerability" button on the Security tab). Include what you found, how to reproduce it and what you think the impact is.

## Known limits

- The dashboard has no login unless you turn authentication on (a trusted reverse proxy, or tokens; see `Controller:Web:Auth` in the configuration). Without it, keep the dashboard on a network you trust.
- Without TLS (`Transport:Mode` `none`) agent tokens travel in clear text. Use `tls` or `mtls` outside a closed network.
- Revocation is not checked for certificates from a private authority; use short-lived certificates.
