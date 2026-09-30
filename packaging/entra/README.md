# Entra ID app registration branding

`app-logo.png` — 215×215, for the app registration's **Branding & properties → Upload new logo**.
It is one of the three renderings of the TodoWerk mark, in the CloudWerk house style;
[../render-icons.py](../render-icons.py) draws all three from one geometry and records the style's
measurements.

This is the **app's** logo, not the publisher's. It appears beside the app name on the Microsoft
consent dialog, on the Enterprise applications page and in My Apps — while the *publisher* is named
separately on that same dialog, with the verified badge that Microsoft Entra ID publisher
verification adds. So it carries the
TodoWerk mark rather than the CloudWerk one, and matches the Teams package's icon so that the tab and
the consent dialog read as one product.

Full-bleed rather than transparent, deliberately: the consent dialog paints it on white, where a
transparent tile would leave a white glyph on white.

A Self-Host registers its own Entra ID application
([teams-app-registration.md](../../docs/runbooks/teams-app-registration.md)) and can upload this same
file, or its own — nothing about the logo is deployment-specific.
