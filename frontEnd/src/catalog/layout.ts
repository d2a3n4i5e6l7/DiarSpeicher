/**
 * Pistas de rejilla del catálogo.
 *
 * `auto-fill` con un mínimo por tarjeta: el número de columnas lo decide el ancho
 * disponible, no una lista de breakpoints. En un monitor ultrapanorámico entran las
 * que quepan en vez de quedarse en tres, y en un móvil cae a una sin caso especial.
 * El `1fr` reparte el sobrante para que no queden huecos a la derecha.
 */
export const COVER_GRID = "repeat(auto-fill, minmax(170px, 1fr))";

/** Las tarjetas de biblioteca llevan contadores y botones: necesitan más ancho mínimo. */
export const PANEL_GRID = "repeat(auto-fill, minmax(330px, 1fr))";

/**
 * Un texto corrido no debe estirarse a lo ancho de un ultrapanorámico: pasados unos
 * 90 caracteres por línea el ojo pierde el renglón al volver. Solo lo llevan los
 * bloques de lectura y los formularios, nunca las rejillas.
 */
export const READABLE_WIDTH = "min(100%, 1100px)";
