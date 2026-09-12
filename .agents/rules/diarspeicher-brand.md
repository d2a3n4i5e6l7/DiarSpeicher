# Regla de Identidad Visual: DiarSpeicher (Iron Blood Edition)

Todo el frontend de DiarSpeicher debe seguir estrictamente el Brandbook Oficial.

## 1. Paleta de Colores Exclusiva
- Fondo base: `#050508` (`Schwarz Core`)
- Superficie hundida (cabeceras de tabla, inputs, pies): `#0A0B0E`
- Superficies y tarjetas: `#0F1015` / `#161821` (hover `#1E212D`)
- Acento de marca: `#C21818` (`Blutrot`)
- Iluminación, bordes activos y focos: `#FF2E2E` / `#FF3E3E` (`Red Glow`)
- Sombras y bordes sutiles: `#660B0B` / `#282C38` / `#1C1F28`
- Rojo profundo para gradientes y cabeceras: `#1C0303`
- Texto principal: `#F0F2F6` (`Platinum`)
- Texto secundario: `#8E95A5` / `#A3ABB8`; texto tenue: `#636B7C`
- Rampa metálica (bisel de bordes e isotipo): `#8F96A3` → `#4B5160` → `#2B303E` → `#121318`
- Gradiente oficial: `linear-gradient(135deg, #C21818 0%, #1A0202 50%, #050508 100%)`
- **Prohibido**: Verdes (#10B981, etc.), azules o cianes como acentos principales.
  El verde y el ámbar solo se admiten como semáforo de estado (`READY`, `MISSING`,
  `NODE: ONLINE`, alertas de éxito/aviso), nunca como color de marca.

## 1.b Modo día (`Tageslicht`)

La aplicación tiene dos modos. El de noche es el de arriba. El de día no lo aclara:
lo cambia de material, tomando la paleta del uniforme de Iron Blood.

- Fondo base: `#ECEEF2` (`Porzellan`) — nunca blanco puro, deslumbra en pantallas grandes
- Superficie hundida: `#DFE3EA`; tarjetas: `#F7F8FA`; paneles: `#F2F4F7`
- Bordes y biseles: `#B7BEC9` / `#CDD3DC`, metal `#9AA3B0` (`Stahlgrau`)
- Acento de marca: `#A32020` (`Rot gedämpft`) — 6.49:1 sobre porcelana
- Texto: `#16181D`; secundario `#4A505C` (6.97:1); tenue `#767E8C`
- Acento frío: `#2F5C90` (`Eisblau`, el iris) — 5.92:1

Reglas que no se negocian:
- **El rojo de día es tinta, no luz.** Ningún `box-shadow` ni `drop-shadow` rojo.
  Lo hace solo el token `--ds-glow-a`, que vale `0.45` de noche y `0` de día.
- **La luz cae desde arriba**: el bisel metálico se invierte, reflejo blanco en el
  borde superior y sombra acero en el inferior.
- **El azul nunca señala error.** Error es rojo en los dos modos. El iris se usa para
  información y selección, y solo de día (`--ds-info-line`, `--ds-select`).
- **El lector no tiene modo día.** La página ya es lo más claro de la pantalla.
  Su subárbol lleva la clase `ds-force-dark`, que vuelve a declarar los tokens
  oscuros y lo deja en noche aunque `:root` esté en día.
- Los halos radiales terminan en `rgba(<mismo color>, 0)`, nunca en `transparent`:
  `transparent` es `rgba(0,0,0,0)` y al interpolar deja un aro gris que Firefox,
  que no aplica dithering a los degradados, dibuja como un círculo visible.
  Encima va la clase `ds-noise` para romper las bandas que queden.

## 2. Tipografía
- Logotipos y Brand Display: `'Orbitron'`, sans-serif
- Títulos de sección, metadatos y badges: `'Rajdhani'`, sans-serif (700, mayúsculas, tracking aumentado)
- Códigos, hashes, IDs de nodo, contadores y horas: `'JetBrains Mono'`, monospace
- Lectura y cuerpo de texto: `'Inter'` o `'Chakra Petch'`

## 3. Geometría y Key Visuals
- Ángulos a 45° (achaflanados con `clip-path`) en botones, chips, alertas, tarjetas y diálogos.
- **Bisel metálico** en el borde de tarjetas y diálogos: el filo es la rampa metálica
  pintada como fondo del elemento, y un `::before` a `inset: 1px` pinta la cara interior.
  No sirve un `border`: el chaflán lo recorta en la diagonal y esta queda sin filo.
- Marcadores de esquina HUD: **los cuatro** (`hud-corner-tl`, `-tr`, `-bl`, `-br`).
  En el frontend se ponen con el componente `<HudFrame />`.
- Rejilla táctica de 40px y halo carmesí superior sobre el área de contenido: sin ellos
  el fondo queda como un negro plano.
- Isotipo oficial: «D» blindada con cruz de hierro carmesí central.

## 4. Dónde vive esto en el código
- Valores de los dos modos: `frontEnd/src/theme/tokens.ts` (`DARK` y `DAY`).
- Biseles, clases HUD y overrides de MUI: `frontEnd/src/theme/index.ts`
  (exporta `DS`, y `buildTheme(mode)` publica los tokens del modo en `:root`).
- **`DS.*` son referencias a variables CSS, no colores literales.** Por eso una
  página escrita con `DS` cambia de modo sin tocarla. No pasarlos por `alpha()`
  ni meterlos dentro de un `rgb()`: para eso están `--ds-red-rgb` y compañía.
- En SVG hay que escribir `style={{ fill: "var(--ds-red)" }}`, no `fill="var(...)"`:
  como atributo de presentación no todos los navegadores lo resuelven.
- Marco HUD: `frontEnd/src/components/HudFrame.tsx`.
- Cabecera de página: `frontEnd/src/components/PageHeader.tsx`.
