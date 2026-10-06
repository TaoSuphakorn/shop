/**
 * Content-Security-Policy for pages (spec 11.3). Set here only, never also at the ingress:
 * two policies are both enforced and the nonce would stop working.
 * style-src keeps 'unsafe-inline' because the UI components inject styles at runtime.
 */
export function buildCsp(nonce: string, isDevelopment: boolean): string {
  return [
    "default-src 'self'",
    `script-src 'self' 'nonce-${nonce}' 'strict-dynamic'${isDevelopment ? " 'unsafe-eval'" : ""}`,
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' data:",
    "font-src 'self'",
    "connect-src 'self'",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "frame-ancestors 'none'",
  ].join("; ");
}
