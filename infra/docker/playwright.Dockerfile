# package-lock.json resolves @playwright/test to 1.64.0, so use the matching Linux image tag here.
FROM mcr.microsoft.com/playwright:v1.64.0-noble

WORKDIR /work

ENV CI=true

CMD ["bash"]
