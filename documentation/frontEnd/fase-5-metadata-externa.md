# Fase 5 · Metadata externa

## Punto de partida

`ExtractedMetadata` lee `ComicInfo.xml`, pero ese fichero aparece en una fracción mínima de
los `.cbz` reales: en la biblioteca de pruebas `MediaMetadata` está vacío y el nombre del
libro es el del fichero. Las fases 3 y 4 dejan huecos deliberados —editorial, autores,
géneros, estado, relacionados— esperando esta fase.

## Servicio externo

La metadata la sirve un **contenedor aparte**, con una API REST cuya única función es
consultar el volcado de [MangaBaka](https://mangabaka.org/data/database) y responder por
POST. El frontend no habla con mangabaka.org ni conoce su esquema: habla con ese servicio.

Datos del volcado, verificados: SQLite de 3,3 GB descomprimido, 559.776 series (365.147
manga, 61.453 novelas, 59.772 manhwa, 24.960 manhua), agregando AniList, MangaUpdates,
MyAnimeList, Kitsu, Anime-Planet, Shikimori y Anime News Network. Licencia CC BY-NC-SA 4.0,
uso no comercial con atribución.

Por serie ofrece título nativo y romanizado, títulos secundarios en veinticinco idiomas,
resumen, autores, artistas, géneros, etiquetas, estado, valoración, capítulos totales,
volumen final, fechas de publicación, relaciones entre obras y portadas pregeneradas en
nueve variantes con blurhash.

No hay descarga incremental: cada actualización es el volcado completo. Para una biblioteca
de obras terminadas eso no importa, y refrescar cada pocos meses es suficiente.

## Trabajo en el frontend

**Emparejado asistido, nunca automático.** El nombre de una carpeta no identifica una obra
sin ambigüedad: `Galaxy Angel` devuelve cinco candidatos en el volcado —la serie original,
*Beta*, *Party*, *II* y *2nd*—. La interfaz muestra los candidatos con portada, año y tipo,
y la elección la hace una persona. El emparejado se guarda para no repetirlo.

**Botón de buscar metadata** en la ficha de serie y en la de biblioteca, para emparejar en
lote las series sin identificar.

**Relleno de los huecos** que las fases anteriores dejaron: chips de editorial, autores,
géneros y estado en la cabecera; pestañas `Relacionados` y `Reseñas`; portada externa como
alternativa a la extraída del archivo.

**Indicador de completitud.** El volcado trae `final_volume`, así que la ficha puede decir
"3 de 5" y marcar las series incompletas. Es de las cosas más útiles que aporta y no se
puede deducir de los ficheros.

**Distinción de origen.** Un dato que viene del servicio externo se marca como tal y puede
revertirse a lo que dijo el archivo. Una metadata externa equivocada no debe destruir la
que venía embebida.

## Hecho cuando

Se empareja una serie contra el catálogo eligiendo entre candidatos, la ficha se rellena con
los datos externos, y se distingue de un vistazo lo que vino del archivo de lo que vino del
servicio.
