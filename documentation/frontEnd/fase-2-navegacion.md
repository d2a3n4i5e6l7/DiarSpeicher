# Fase 2 · Navegación de la biblioteca

## Punto de partida

`AppLayout` es un panel de administración: drawer permanente con tres entradas y el
contenido a la derecha. La ruta índice redirige a `/users`. No existe ninguna pantalla que
muestre contenido.

## Decisión de estructura

Komga apila las bibliotecas en la barra lateral porque su modelo es el que exige OPDS:
Library → Series → Book. Esa jerarquía se mantiene intacta en la API —los clientes
cDisplayEx, Panels y KOReader dependen de ella— pero deja de gobernar la navegación de la
SPA.

Las bibliotecas salen del drawer y pasan a ser un **filtro**, no un contenedor. El drawer
queda reservado a administración (usuarios, roles, bibliotecas, subida), y el contenido vive
en una home propia agrupada por secciones, al estilo de Audiobookshelf.

## Trabajo

**Home en `/`** con secciones horizontales desplazables, cada una alimentada por un endpoint
que ya existe:

| Sección           | Origen                                     |
| ----------------- | ------------------------------------------ |
| Continuar leyendo | `GET /api/v2/media/keep-reading`           |
| Añadido reciente  | `GET /api/v2/media` ordenado por creación   |
| Series            | `GET /api/v2/series`                        |

La ruta índice deja de redirigir a `/users`.

**Selector de biblioteca en la barra superior**, no en el drawer: un menú que filtra todas
las secciones y persiste la elección en `localStorage`. "Todas" es una opción válida y es el
valor inicial.

**Rejilla de series en `/series`**, con tarjeta de portada
(`GET /api/v2/series/{id}/thumbnail`), título y recuento de tomos. Paginada contra los
parámetros que ya acepta el endpoint.

**Componentes reutilizables** en `components/`: `MediaCard` (portada, título, subtítulo,
progreso opcional) y `SectionRow` (título de sección y carrusel horizontal). Los usan esta
fase y la siguiente.

**El drawer se reorganiza** en dos bloques: contenido arriba (Inicio, Series) y
administración abajo (Usuarios, Roles, Bibliotecas, Subida). `navItems.ts` pasa a agrupar
por sección en vez de ser una lista plana.

## Rendimiento

Las portadas se piden con `loading="lazy"`. Una biblioteca de varios cientos de series no
debe montar cientos de peticiones al entrar: la rejilla pagina y los carruseles piden un
número acotado de elementos.

## Hecho cuando

Al entrar se ve contenido en vez del listado de usuarios, se cambia de biblioteca desde la
barra superior, y desde la rejilla de series se llega a una serie concreta.
