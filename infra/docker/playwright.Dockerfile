# Root package-lock.json resolves @playwright/test 1.64.1. Keep the qualified 1.64.0 Linux image/browser build until the npm package and image are jointly requalified.
FROM mcr.microsoft.com/playwright:v1.64.0-noble

WORKDIR /work

ENV CI=true

CMD ["bash"]
