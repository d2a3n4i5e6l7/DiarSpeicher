# Fase 4 · Lector

## Punto de partida

El backend sirve páginas sueltas (`GET /api/v2/media/{id}/page/{page}`), guarda el progreso
(`PUT /api/v2/media/{id}/progress`) y persiste las dimensiones de cada página en
`MediaPages`. Ningún cliente propio consume nada de eso: hoy solo leen cDisplayEx, Panels y
KOReader por OPDS.

## Trabajo

**Lector a pantalla completa** en `/read/:mediaId`, fuera de `AppLayout`: sin drawer ni
barra superior. La interfaz se oculta sola y reaparece al mover el puntero o tocar.

**Modos de lectura**, tomando el valor inicial de `defaultReadingMode` de la biblioteca:

- Paginado, una página por vista.
- Doble página, para lectura apaisada.
- Tira continua vertical, para manhwa.

La dirección (`defaultReadingDir`) invierte los controles en las obras de derecha a
izquierda.

**Precarga.** Se piden por adelantado las páginas siguientes —tres bastan— para que el
avance no espere a la red. Las dimensiones guardadas en `MediaPages` permiten reservar el
hueco de cada página antes de que la imagen llegue, evitando el salto de maquetación; ese
es justamente el motivo por el que se persisten.

**Persistencia del progreso**: se envía al cambiar de página, con retardo para no disparar
una petición por cada avance, y también al salir del lector.

**Controles**: teclado (flechas, espacio, `f` para pantalla completa, `Esc` para salir),
gesto de deslizar en táctil, barra de navegación con miniatura de la página y salto directo.

**Ajustes del lector** en un panel lateral: modo, dirección, ajuste de imagen (ancho, alto,
original) y color de fondo. Se guardan por usuario en `localStorage`, no en el servidor.

## EPUB

Queda fuera de esta fase. El backend ya expone `GET /api/v2/epub/{id}/toc` y
`/epub/{id}/resource/{*path}`, pero un lector de EPUB es un trabajo distinto al de imágenes
y no comparte casi nada con él.

## Hecho cuando

Se abre un tomo desde su ficha, se lee de principio a fin sin esperas perceptibles entre
páginas, y al volver a entrar la lectura continúa donde se dejó.
