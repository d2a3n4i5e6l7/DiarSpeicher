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
- Tokens, biseles, clases HUD y overrides de MUI: `frontEnd/src/theme/index.ts`
  (exporta `DS` con los tokens para usarlos desde los `sx`).
- Marco HUD: `frontEnd/src/components/HudFrame.tsx`.
- Cabecera de página: `frontEnd/src/components/PageHeader.tsx`.
