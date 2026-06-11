# GitHub Pages Deployment

ARIEC60870 includes a static landing page under `landing/`.

Recommended setup:

1. Open the repository on GitHub.
2. Go to **Settings → Pages**.
3. Set **Source** to **GitHub Actions**.
4. Push to `main` or `master`, or run the **Deploy landing page** workflow manually.

Compatibility options are also included:

- If Pages is set to **Deploy from branch → root**, the root `index.html` redirects to `landing/`.
- If Pages is set to **Deploy from branch → docs**, `docs/index.html` serves the same landing page.

If the site still shows 404:

- Confirm the selected branch is the branch that contains the files.
- Confirm the Pages build/deploy action completed successfully.
- Wait a few minutes after the first deployment.
- Open the repository URL without adding `/landing` when using GitHub Actions mode.
