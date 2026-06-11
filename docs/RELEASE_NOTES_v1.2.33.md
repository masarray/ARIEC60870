# ARIEC60870 v1.2.33 - SEO and Discovery Hardening

This release improves how ARIEC60870 is presented to users who discover the project through GitHub, search engines, and the public landing page.

## What changed

- README title and opening copy now include clearer IEC 60870-5-103, IEC-103, protection relay, FAT/SAT, SCADA, and evidence wording.
- Landing page now includes canonical URL, Open Graph URL, absolute preview image metadata, Twitter preview image metadata, and richer SoftwareApplication structured data.
- Added visible FAQ section for common download-and-evaluation questions.
- Added robots.txt, sitemap.xml, and web manifest files for the landing-page deployment.
- Added a GitHub SEO checklist with recommended repository description, website URL, and topics.
- Updated GitHub Pages workflow paths so SEO files trigger deployment when changed.

## How to try it

1. Download the Windows portable ZIP from GitHub Releases.
2. Extract the package to a local folder.
3. Run `Start-ARIEC60870.bat`.
4. Configure COM port, baudrate, link address, common address, timeout, GI option, and optional mapping profile.
5. Start the IEC-103 session and review Operator Evidence, Value Viewer, Relay Event Log, Frame Trace, and Diagnostics.
6. Export evidence after the test session when needed.

## Release assets

```text
ARIEC60870-v1.2.33-win-x64-portable.zip
SHA256SUMS.txt
```
