# Fase 3 · Ficha de serie

## Punto de partida

Fase 2 deja la navegación llegando hasta la serie, pero sin pantalla que la muestre. El
backend ya sirve `GET /api/v2/series/{id}` y `GET /api/v2/series/{id}/media`.

## Trabajo

**Cabecera de la serie** en `/series/:id`: portada grande a la izquierda y, a la derecha,
título, fila de metadatos como chips, resumen plegable y botones de acción.

La fila de chips se construye con lo que hay: número de tomos, páginas totales (suma de
`Media.Pages`), tiempo estimado de lectura y año. El resto de chips que muestran Kavita o
Komga —editorial, autores, géneros, estado— dependen de metadatos que hoy están vacíos y
llegan en la fase 5. **Un chip sin dato no se renderiza**, para que la cabecera no quede
llena de guiones.

**Progreso de la serie**: barra sobre la portada con el porcentaje leído y el enlace
"Continuar por *X*", calculado a partir de las sesiones de lectura que devuelve el backend.

**Pestañas** bajo la cabecera. De las que muestra Kavita solo dos tienen datos que respaldar
hoy:

| Pestaña  | Contenido                                              |
| -------- | ------------------------------------------------------ |
| Tomos    | Rejilla de medios de la serie, con progreso por tomo    |
| Detalles | Ruta en disco, tamaño, formato, fechas, hash            |

`Relacionados` y `Reseñas` quedan fuera: no hay datos. Se añaden en la fase 5, cuando
MangaBaka aporte `relationships` y `rating`.

**Ficha de medio** en `/media/:id`: la misma estructura reducida a un tomo, con el botón de
lectura, las dimensiones de página que ya persiste `MediaPages` y el acceso a la descarga
(`GET /api/v2/media/{id}/file`).

## Ordenación

Los tomos se listan por el orden natural que ya calcula el backend, no alfabéticamente: la
API los devuelve ordenados y el frontend respeta ese orden sin reordenar por su cuenta.

## Hecho cuando

Desde la rejilla se abre una serie, se ven sus tomos con el progreso de cada uno, y desde
ahí se llega a la ficha de un tomo concreto.
