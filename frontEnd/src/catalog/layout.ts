/**
 * `auto-fill` en vez de breakpoints: las columnas las decide el ancho real, así que un
 * ultrapanorámico entra las que quepan en vez de quedarse en tres.
 */
export const COVER_GRID = "repeat(auto-fill, minmax(170px, 1fr))";

/** Las tarjetas de biblioteca llevan contadores y botones: necesitan más ancho mínimo. */
export const PANEL_GRID = "repeat(auto-fill, minmax(330px, 1fr))";

/**
 * Solo para bloques de lectura y formularios, nunca para rejillas: pasados unos 90
 * caracteres por línea el ojo pierde el renglón al volver.
 */
export const READABLE_WIDTH = "min(100%, 1100px)";

/** No usar en páginas que ya tienen contenido a lo ancho: el bloque centrado se desalinea. */
export const READABLE_COLUMN = { maxWidth: READABLE_WIDTH, mx: "auto" } as const;
