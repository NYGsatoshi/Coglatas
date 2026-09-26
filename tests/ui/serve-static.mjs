import { createReadStream } from "node:fs";
import { stat } from "node:fs/promises";
import { createServer } from "node:http";
import path from "node:path";

const root = path.resolve(process.env.PLAYWRIGHT_STATIC_ROOT ?? path.join(process.cwd(), "frontend/dist/coglatas-web"));
const host = argValue("--host") ?? process.env.PLAYWRIGHT_HOST ?? "127.0.0.1";
const port = Number(argValue("--port") ?? process.env.PLAYWRIGHT_PORT ?? 4173);
const appPath = "/app";
const staticWorkspace = {
  id: "static-workspace-1",
  name: "Static Workspace",
  description: "Playwright static workspace",
  icon: null,
  status: "Active",
  createdAt: "2026-07-06T00:00:00Z",
  updatedAt: "2026-07-06T00:00:00Z",
  currentUserRole: "Member",
  accessSource: "WorkspaceMembership",
  canOpenWorkspace: true,
  canOpenMembers: true,
  canOpenProjects: true,
  unreadAnnouncementCount: 2,
  unreadConversationCount: 1,
  inProgressProjectCount: 3,
  runningProjectCount: 2,
  needsReviewProjectCount: 1
};

function argValue(name) {
  const index = process.argv.indexOf(name);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

function contentType(filePath) {
  const extension = path.extname(filePath).toLowerCase();
  return {
    ".css": "text/css; charset=utf-8",
    ".html": "text/html; charset=utf-8",
    ".js": "text/javascript; charset=utf-8",
    ".json": "application/json; charset=utf-8",
    ".svg": "image/svg+xml"
  }[extension] ?? "application/octet-stream";
}

function safePath(pathname) {
  const relativePath = pathname === "/" ? "index.html" : pathname.slice(1);
  const filePath = path.resolve(root, relativePath);
  return filePath.startsWith(root + path.sep) || filePath === root ? filePath : null;
}

function frontendPath(pathname) {
  if (pathname === appPath || pathname === `${appPath}/`) {
    return "/";
  }

  if (pathname.startsWith(`${appPath}/`)) {
    return pathname.slice(appPath.length);
  }

  return null;
}

async function existingFile(filePath) {
  try {
    const stats = await stat(filePath);
    if (stats.isDirectory()) {
      return existingFile(path.join(filePath, "index.html"));
    }

    return stats.isFile() ? filePath : null;
  } catch {
    return null;
  }
}

const server = createServer(async (request, response) => {
  const requestUrl = new URL(request.url ?? "/", `http://${request.headers.host ?? `${host}:${port}`}`);
  const pathname = decodeURIComponent(requestUrl.pathname);
  if (pathname === "/health") {
    response.writeHead(200, { "content-type": "application/json; charset=utf-8" });
    response.end(JSON.stringify({ status: "OK" }));
    return;
  }

  if (pathname === "/") {
    response.writeHead(302, { location: `${appPath}/` });
    response.end();
    return;
  }

  if (pathname === "/api/auth/me") {
    response.writeHead(200, { "content-type": "application/json; charset=utf-8" });
    response.end(JSON.stringify({
      userId: "mock-user-a",
      displayName: "Mock User A",
      email: "mock-user-a@example.invalid",
      systemRole: "TenantUser",
      status: "Active",
      capabilities: ["workspace:view", "announcements:view", "projects:view", "files:view", "account:view", "audit:view"],
      currentWorkspace: staticWorkspace,
      workspaces: [staticWorkspace]
    }));
    return;
  }

  if (pathname === "/api/auth/status") {
    response.writeHead(200, { "content-type": "application/json; charset=utf-8" });
    response.end(JSON.stringify({
      isAuthenticated: true,
      user: {
        userId: "mock-user-a",
        displayName: "Mock User A",
        email: "mock-user-a@example.invalid",
        systemRole: "TenantUser",
        status: "Active",
        capabilities: ["workspace:view", "announcements:view", "projects:view", "files:view", "account:view", "audit:view"],
        currentWorkspace: staticWorkspace,
        workspaces: [staticWorkspace]
      }
    }));
    return;
  }

  if (pathname === "/api/tenants/current") {
    response.writeHead(200, { "content-type": "application/json; charset=utf-8" });
    response.end(JSON.stringify({
      tenantId: "mock-tenant",
      tenantSlug: "mock",
      isAvailable: true,
      isPlatformScope: false,
      displayName: "Mock Tenant",
      status: "Active",
      currentUserRole: "Admin",
      appMode: "OnPremSingleTenant",
      allowTenantSwitching: true
    }));
    return;
  }

  if (pathname === "/api/workspaces") {
    response.writeHead(200, { "content-type": "application/json; charset=utf-8" });
    response.end(JSON.stringify([staticWorkspace]));
    return;
  }

  if (pathname === "/api/workspaces/capabilities") {
    response.writeHead(200, { "content-type": "application/json; charset=utf-8" });
    response.end(JSON.stringify({
      requestId: "playwright-workspaces-capabilities",
      data: { canCreate: false },
      warnings: []
    }));
    return;
  }

  if (pathname.startsWith("/api/")) {
    response.writeHead(404, { "content-type": "application/json; charset=utf-8" });
    response.end(JSON.stringify({ error: "Endpoint not found." }));
    return;
  }

  if (pathname.includes("\0")) {
    response.writeHead(400).end("Bad request");
    return;
  }

  const appRelativePath = frontendPath(pathname);
  if (!appRelativePath) {
    response.writeHead(404, { "content-type": "text/plain; charset=utf-8" });
    response.end("Not found");
    return;
  }

  const requestedPath = safePath(appRelativePath);
  if (!requestedPath) {
    response.writeHead(403).end("Forbidden");
    return;
  }

  let filePath = await existingFile(requestedPath);
  if (!filePath) {
    if (path.extname(appRelativePath)) {
      response.writeHead(404, { "content-type": "text/plain; charset=utf-8" });
      response.end("Not found");
      return;
    }

    filePath = await existingFile(path.join(root, "index.html"));
    if (!filePath) {
      response.writeHead(404, { "content-type": "text/plain; charset=utf-8" });
      response.end("Angular build output was not found. Run npm run build in frontend/ before Playwright.");
      return;
    }
  }

  response.writeHead(200, {
    "cache-control": "no-store",
    "content-type": contentType(filePath)
  });

  if (request.method === "HEAD") {
    response.end();
    return;
  }

  createReadStream(filePath).pipe(response);
});

server.listen(port, host, () => {
  console.log(`Serving ${root} at http://${host}:${port}`);
});

for (const signal of ["SIGINT", "SIGTERM"]) {
  process.on(signal, () => {
    server.closeAllConnections?.();
    server.close(() => process.exit(0));
    setTimeout(() => process.exit(0), 1_000).unref();
  });
}
