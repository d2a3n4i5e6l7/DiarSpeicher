# Fase 5 · Metadata externa

## Punto de partida

`ExtractedMetadata` lee `ComicInfo.xml`, pero ese fichero aparece en una fracción mínima de
los `.cbz` reales: en la biblioteca de pruebas `MediaMetadata` está vacío y el nombre del
libro es el del fichero. Las fases 3 y 4 dejan huecos deliberados —editorial, autores,
géneros, estado, relacionados— esperando esta fase.

## Decisión: el volcado vive dentro, no en un servicio aparte

Una versión anterior de este documento planteaba un contenedor aparte con API REST. Se
descarta. El volcado de MangaBaka es un fichero estático que se refresca cada pocos meses:
no necesita un proceso propio, un puerto propio ni un despliegue propio. Montar un servicio
para consultar un SQLite de solo lectura es infraestructura que hay que operar a cambio de
nada.

En su lugar:

- El volcado se **descarga a mano**, desde un botón de la interfaz, y queda en la carpeta
  `manga_database/` del propio servidor. Nunca se distribuye con el repositorio: son varios
  gigabytes y una licencia que no es nuestra.
- Se consulta **desde el GraphQL que ya existe** (HotChocolate, montado en `Program.cs`),
  como un tipo más junto a `Series`, `Media` y `Library`. El frontend ya habla GraphQL; no
  hay que enseñarle un protocolo nuevo.

## El volcado

Origen: <https://mangabaka.org/data/database>. Licencia CC BY-NC-SA 4.0 — uso no comercial
con atribución, que la interfaz debe mostrar donde se enseñen los datos.

Cifras verificadas sobre el fichero real:

| Dato | Valor |
| --- | --- |
| Comprimido (`.zst`) | ~390 MB |
| Descomprimido (`series.sqlite`) | ~3,5 GB |
| Filas | 559.776 series |
| Estructura | **una sola tabla** `series`, ~140 columnas |
| Índices | **ninguno** |

Que no traiga índices es lo que condiciona el diseño: buscar por título tal cual es un
escaneo secuencial de 3,5 GB. La ingesta tiene que construir su propio índice —una tabla
FTS5 sobre los títulos— o el emparejado es inusable.

Por serie ofrece título nativo y romanizado, títulos secundarios en veinticinco idiomas,
resumen, autores, artistas, géneros, etiquetas, estado, valoración, capítulos totales,
volumen final, fechas de publicación, relaciones entre obras y portadas pregeneradas en
nueve variantes con blurhash.

No hay descarga incremental: cada actualización es el volcado completo.

## Obtención del volcado

Dos caminos, ambos disparados por el usuario, nunca automáticos:

1. **Botón «Descargar base de datos de manga»**: el servidor baja el `.zst` de mangabaka.org,
   lo descomprime y lo deja en `manga_database/`. La descarga informa de su progreso: son
   cientos de megabytes y el usuario tiene que poder ver que avanza.
2. **Arrastrar un fichero**: se acepta `.zst` y `.tar.gz` ya descargados por otros medios, y
   el servidor los descomprime igual. Cubre el caso de una máquina sin salida a internet y
   el de quien ya tiene el volcado.

`SharpCompress`, que ya es dependencia del proyecto, cubre los dos formatos
(`Compressors.ZStandard.ZStandardStream`), así que no entra ninguna librería nueva.

Tras descomprimir, la ingesta construye el índice de búsqueda. Es el paso lento y va con
progreso propio, igual que un escaneo de biblioteca.

## Emparejado

**Asistido, nunca automático.** El nombre de una carpeta no identifica una obra sin
ambigüedad: `Galaxy Angel` devuelve cinco candidatos —la serie original, *Beta*, *Party*,
*II* y *2nd*—. La interfaz muestra los candidatos con portada, año y tipo, y la elección la
hace una persona. El emparejado se guarda para no repetirlo.

**Botón de buscar metadata** en la ficha de serie y en la de biblioteca, para emparejar en
lote las series sin identificar.

**En la creación de biblioteca**, el mismo catálogo alimenta los campos que hoy se rellenan
a mano: tipo de contenido y dirección de lectura se deducen del `type` de MangaBaka
(`manga` y `manhwa` leen de derecha a izquierda; `manhua` y novelas, al revés).

## Relleno de los huecos

Chips de editorial, autores, géneros y estado en la cabecera de serie; pestañas
`Relacionados` y `Reseñas`; portada externa como alternativa a la extraída del archivo.

**Indicador de completitud.** El volcado trae `final_volume`, así que la ficha puede decir
"3 de 5" y marcar las series incompletas. Es de las cosas más útiles que aporta y no se
puede deducir de los ficheros.

**Distinción de origen.** Un dato que viene del volcado se marca como tal y puede revertirse
a lo que dijo el archivo. Una metadata externa equivocada no debe destruir la que venía
embebida.

## Hecho cuando

Se descarga el volcado desde la interfaz, se empareja una serie contra el catálogo eligiendo
entre candidatos, la ficha se rellena con los datos externos, y se distingue de un vistazo
lo que vino del archivo de lo que vino del volcado.
