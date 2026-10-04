/**
 * Anchors only exist on the home page. On /support, /privacy and /terms a bare
 * "#prices" points at an id that is not in the document: the browser updates
 * the hash and scrolls nowhere, so the link looks alive and does nothing.
 * Prefixing with "/" turns it into a real cross-page navigation that lands on
 * the home page's anchor.
 *
 * Shared by the header and the footer, which both render on every route — the
 * footer used to hardcode bare hashes and was silently dead on three of the
 * four pages.
 */
export const homeAnchor = (pathname: string, hash: string): string =>
  pathname === '/' ? hash : `/${hash}`;