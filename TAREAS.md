# Avisos de Sonar

62 avisos reales. Se ejecuta con `./sonar.sh`; el paquete va condicionado a `-p:Sonar=true`
para no romper el `-warnaserror` de `lint.sh`.

Ya cerrados: S6444 (13 regex sin timeout, ReDoS sobre el CSS del libro) y las ~2950 reglas
CA que salian de un `AnalysisMode=All` que sobraba.

## S1 · Restos de los cambios de esta semana

Codigo muerto introducido en los refactors recientes. Se borra y ya.

- `BookProcessors.cs:413` S1481 — `measurePages` sin usar, resto de mover las banderas a
  `BookAnalysisOptions`.
- `PartialUploadPurgeService.cs:135` S1144 — el `set` de `PartPath` no lo usa nadie.
- `ImageHeaderTests.cs:114` S2699 — `UnJpegCortadoNoRevienta` no tiene ninguna asercion.
  Comprueba que no lanza, pero eso no lo dice.
- `ImageHeaderTests.cs:97` S4144 — los tests de GIF y BMP tienen cuerpo identico. Unificar en
  un solo `[Theory]` con el formato como parametro.

## S2 · Defectos reales

- `FolderIndex.cs:132` y `:350` S8949 — no se propaga el `CancellationToken`. El `:132` es el
  `Task.Run` del repaso: sin token no se entera de la cancelacion aunque `_lifetime` se cancele.
- `DatabaseBackupService.cs:85` y `FolderIndex.cs:143` S6667 — se registra dentro de un `catch`
  sin pasar la excepcion. Se pierde el stack trace donde mas falta hace.
- `MangaBakaCatalog.cs:68` S2696 — un metodo de instancia escribe un campo `static`
  (`_countCache`): dos peticiones concurrentes pueden pisarse.
- `EpubFontProvider.cs:233` S1481 — variable `on` sin usar.

## S3 · Falsos positivos, suprimir con justificacion

No se arreglan: se silencian con el motivo, porque el analizador no puede saberlo.

- `MangaBakaCatalog.cs:113` y `:130` S2077 — lo unico interpolado es `{limit}`, que viene de
  `Math.Clamp(limit, 1, 50)`. La entrada del usuario va parametrizada (`$needle`, `$like`).
- `EpubFontProvider.cs:291` S4790 — el SHA-1 lo exige la especificacion IDPF para deshacer el
  enmascarado de fuentes. No es una eleccion criptografica nuestra.
- `EpubFontProvider.cs:18` y `:233` S5332 — esos `http://` son namespaces XML
  (`http://www.idpf.org/2007/opf`), identificadores que nunca se visitan. Pasarlos a https
  romperia el parseo de todos los EPUB.

## S4 · Mecanicos

Una pasada, sin decisiones.

- S6966 (~30, casi todo en tests) — `ZipFile.Open` -> `OpenAsync`, `Write` -> `WriteAsync`.
  El unico de produccion es `ArchiveConversionService.cs:120`.
- S8969 (6, tests) — operador `!` redundante donde el compilador ya sabe que no es nulo.
- S2325 (3, tests) — metodos que pueden ser `static`.
- S3267 (2) — `GatewayOptions.cs:50` y `LibraryRootsOptions.cs:60`: bucle con `if` dentro que
  se lee mejor con `Where`.
- `KomgaEndpoints.cs:49` S3358 — ternario anidado.
- `DiarSpeicherEndpoints.cs:541` S3241 — devuelve un valor que ningun llamante usa.
- `Program.cs:157` S1075 — separador de ruta escrito a mano.
- `ScanProgressHub.cs:43` S1135 — un TODO.

## Orden

S1 y S2 primero: son los unicos que cambian comportamiento o borran codigo muerto de verdad.
S3 despues, para que el recuento no mienta. S4 al final, de una tacada.
