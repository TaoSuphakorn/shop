import { NextRequest, NextResponse } from "next/server";
import { buildCsp } from "@/lib/csp";

export function proxy(request: NextRequest) {
  const { pathname, search } = request.nextUrl;

  // Without an ingress in front (local dev, docker compose) the browser's same-origin /api calls are
  // forwarded to the API here. Read at request time so one image works in every environment.
  if (pathname.startsWith("/api/")) {
    return NextResponse.rewrite(new URL(pathname + search, process.env.API_ORIGIN ?? "http://localhost:5080"));
  }

  const nonce = Buffer.from(crypto.randomUUID()).toString("base64");
  const csp = buildCsp(nonce, process.env.NODE_ENV === "development");

  const requestHeaders = new Headers(request.headers);
  requestHeaders.set("x-nonce", nonce);
  requestHeaders.set("Content-Security-Policy", csp);   // Next.js reads the nonce from here for its own scripts

  const response = NextResponse.next({ request: { headers: requestHeaders } });
  response.headers.set("Content-Security-Policy", csp);
  response.headers.set("X-Content-Type-Options", "nosniff");
  response.headers.set("Referrer-Policy", "strict-origin-when-cross-origin");
  return response;
}

export const config = {
  matcher: ["/((?!_next/static|_next/image|favicon.ico|products/).*)"],
};
