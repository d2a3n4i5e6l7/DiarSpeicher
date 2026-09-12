---
name: diarspeicher-brand-style
description: Manual de estilo y directrices oficiales de diseño para DiarSpeicher (Iron Blood Edition). Define la paleta de colores (Schwarz Core, Blutrot, Red Glow), tipografía (Orbitron, Rajdhani, JetBrains Mono, Chakra Petch, Inter), geometría facetada a 45°, biseles metálicos, marcadores de esquina HUD y componentes para el lector y catálogo de manga y libros.
---

# DiarSpeicher — Brandbook Oficial (Iron Blood Edition)

Esta skill define el sistema de diseño exclusivo, identidad visual y reglas de maquetación para **DiarSpeicher** (Diarmund Archive Protocol / Iron Blood Edition).

## 1. Filosofía de Marca
- **Ecosistema**: Servidor y visor web de manga, cómics y ebooks autohospedado (compatible con clientes tipo Komga / Kavita / OPDS).
- **Estética**: Fusión de alta ingeniería naval táctica de *Iron Blood*, búnker digital militarizado y minimalismo funcional de alto rendimiento.
- **Atmósfera**: Imponente, oscura, cinematográfica, ortogonal y precisa.

---

## 2. Paleta Cromática y Tokens CSS

```css
:root {
  /* Fondos principales */
  --bg-dark: #050508;      /* Schwarz Core: fondo base absoluto */
  --bg-card: #0F1015;      /* Fondo de tarjetas de manga y estanterías */
  --bg-surface: #161821;   /* Superficie de paneles, drawers y modales */
  --bg-surface-hover: #1E212D;
  --bg-sunken: #0A0B0E;    /* Cabeceras de tabla, inputs y pies de panel */

  /* Acentos DiarSpeicher (Rojos Carmesí) */
  --ds-red: #C21818;       /* Blutrot: acento principal, botones primarios */
  --ds-red-glow: #FF2E2E;  /* Red Glow: estados activos, resplandor, foco */
  --ds-red-light: #FF3E3E;
  --ds-red-dark: #660B0B;  /* Sombras y bordes de contención */
  --ds-red-deep: #1C0303;  /* Base de gradientes oscuros */

  /* Textos */
  --ds-platinum: #F0F2F6;  /* Texto principal de alto contraste */
  --ds-muted: #8E95A5;     /* Texto secundario y metadatos */
  --ds-subtle: #636B7C;    /* Texto tenue, etiquetas secundarias */

  /* Bordes */
  --ds-border: #282C38;
  --ds-border-soft: #1C1F28;
  --ds-border-red: #381010;
  --ds-border-active: #C21818;

  /* Rampa metalica: es la que dibuja el bisel de los bordes */
  --ds-metal-hi: #8F96A3;
  --ds-metal: #4B5160;
  --ds-metal-lo: #121318;
  --ds-bevel-metal: linear-gradient(145deg, #4B5160 0%, #2B303E 38%, #1A1D26 62%, #121318 100%);

  /* Gradientes oficiales */
  --ds-gradient: linear-gradient(135deg, #C21818 0%, #1A0202 50%, #050508 100%);
  --ds-gradient-header: linear-gradient(90deg, #1C0303 0%, #050508 100%);
  --ds-gradient-card: linear-gradient(145deg, #161922 0%, #0A0B0E 100%);

  /* Tipografías */
  --font-display: 'Orbitron', 'Chakra Petch', sans-serif;
  --font-tactical: 'Rajdhani', sans-serif;
  --font-mono: 'JetBrains Mono', monospace;
  --font-body: 'Inter', sans-serif;
}
```

---

## 3. Tipografía Oficial y Jerarquía

| Rol | Familia Tipográfica | Peso | Transform / Tracking | Uso |
| --- | ------------------- | ---- | -------------------- | --- |
| **Logotexto / Master Identity** | `'Orbitron'` | 700, 900 | Uppercase, `letter-spacing: 2px` | Logotipo DIARSPEICHER, títulos de splash |
| **Títulos de Sección y Manga** | `'Rajdhani'` | 700 | Uppercase, `letter-spacing: 1px-2px` | Títulos de fila, nombres de serie, encabezados |
| **Metadatos y Badges** | `'Rajdhani'` | 600, 700 | Uppercase, `letter-spacing: 1px` | Géneros, autores, estado de publicación |
| **Datos Técnicos y Códigos** | `'JetBrains Mono'` | 400, 700 | Monospace | Hashes (MD5, Koreader), páginas, pesos, IDs |
| **Cuerpo y Controles** | `'Inter'` / `'Chakra Petch'` | 400, 600 | Normal | Sinopsis, controles del lector, formularios |

---

## 4. Geometría Táctica y Key Visuals

### Achaflanado a 45° (Corte Balístico)
Tanto botones tácticos como tarjetas destacadas deben utilizar esquinas achaflanadas a 45°:
```css
/* Botón táctico */
.btn-tactical {
  background: #151821;
  color: #F0F2F6;
  border: 1px solid #383E4C;
  font-family: 'Rajdhani', sans-serif;
  font-weight: 700;
  font-size: 14px;
  text-transform: uppercase;
  letter-spacing: 1px;
  padding: 6px 14px;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  gap: 6px;
  transition: all 0.2s ease;
  clip-path: polygon(6px 0%, 100% 0%, 100% calc(100% - 6px), calc(100% - 6px) 100%, 0% 100%, 0% 6px);
}

.btn-tactical:hover {
  background: #C21818;
  border-color: #FF2E2E;
  color: #FFFFFF;
  box-shadow: 0 0 14px rgba(229, 9, 20, 0.5);
}
```

### Bisel Metálico de Bordes (Corte Blindado)
Tarjetas, paneles y diálogos llevan filo metálico en todo el contorno, chaflán incluido.
El filo **no puede ser un `border`**: el `clip-path` lo recorta en la diagonal y esa arista
queda sin filo. El filo es el propio fondo del elemento y un `::before` a `inset: 1px`
pinta la cara interior:

```css
.ds-bevel {
  position: relative;
  background-color: #121318;
  background-image: linear-gradient(145deg, #4B5160 0%, #2B303E 38%, #1A1D26 62%, #121318 100%);
  border: none;
  border-radius: 0;
  clip-path: polygon(14px 0%, 100% 0%, 100% calc(100% - 14px), calc(100% - 14px) 100%, 0% 100%, 0% 14px);
}

.ds-bevel::before {
  content: '';
  position: absolute;
  inset: 1px;                         /* grosor del filo */
  background-image: linear-gradient(145deg, #161922 0%, #0A0B0E 100%);
  clip-path: polygon(13px 0%, 100% 0%, 100% calc(100% - 13px), calc(100% - 13px) 100%, 0% 100%, 0% 13px);
  pointer-events: none;
  z-index: 0;
}

/* El contenido sube por encima de la cara interior. Los marcadores HUD quedan
   fuera de la regla: si no, pierden su `position: absolute` y se descolocan. */
.ds-bevel > *:not([class*="hud-corner"]) {
  position: relative;
  z-index: 1;
}

/* Al pasar por encima, el filo se enciende en carmesí. */
.ds-bevel:hover {
  background-image: linear-gradient(145deg, #8F96A3 0%, #C21818 45%, #660B0B 75%, #1A0202 100%);
  box-shadow: 0 8px 30px rgba(194, 24, 24, 0.22);
}
```

Chaflán de referencia: **14px** en tarjetas, **16px** en diálogos, **8px** en alertas,
**6px** en botones y **4px** en chips y pastillas.

### Marcadores de Esquina HUD
Van **los cuatro**, no dos. El contenedor debe tener `position: relative`:

```css
.hud-corner-tl, .hud-corner-tr, .hud-corner-bl, .hud-corner-br {
  position: absolute;
  width: 14px;
  height: 14px;
  pointer-events: none;
  z-index: 2;
}
.hud-corner-tl { top: 0;    left: 0;  border-top: 2px solid #C21818;    border-left: 2px solid #C21818; }
.hud-corner-tr { top: 0;    right: 0; border-top: 2px solid #C21818;    border-right: 2px solid #C21818; }
.hud-corner-bl { bottom: 0; left: 0;  border-bottom: 2px solid #C21818; border-left: 2px solid #C21818; }
.hud-corner-br { bottom: 0; right: 0; border-bottom: 2px solid #C21818; border-right: 2px solid #C21818; }
```

### Fondo Táctico (Rejilla + Halo)
Sin esto el lienzo queda como un negro plano. Va en el área de contenido, fijo, sin
arrastrar con el scroll:

```css
.ds-grid-bg {
  background-image:
    linear-gradient(to right, rgba(194, 24, 24, 0.03) 1px, transparent 1px),
    linear-gradient(to bottom, rgba(194, 24, 24, 0.03) 1px, transparent 1px);
  background-size: 40px 40px;
}

.ds-glow-bg {
  background-image: radial-gradient(circle at 50% 0%, rgba(194, 24, 24, 0.08) 0%, transparent 55%);
}
```

### Pastilla Monoespaciada (IDs, hashes, contadores)
```css
.ds-pill-mono {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  background: #1B1E26;
  color: #FF2E2E;
  border: 1px solid #660B0B;
  font-family: 'JetBrains Mono', monospace;
  font-size: 11px;
  font-weight: 700;
  padding: 2px 8px;
  clip-path: polygon(4px 0%, 100% 0%, 100% calc(100% - 4px), calc(100% - 4px) 100%, 0% 100%, 0% 4px);
}
```

### Isotipo Oficial DiarSpeicher (Monograma «D» con Cruz Carmesí)
SVG canónico para avatares, favicon y navbar:
```svg
<svg viewBox="0 0 200 200" width="32" height="32">
  <defs>
    <linearGradient id="dsMetal" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0%" stop-color="#4B5160"/>
      <stop offset="50%" stop-color="#262933"/>
      <stop offset="100%" stop-color="#121318"/>
    </linearGradient>
    <linearGradient id="dsCrimson" x1="0" y1="0" x2="1" y2="0">
      <stop offset="0%" stop-color="#80060A"/>
      <stop offset="50%" stop-color="#C21818"/>
      <stop offset="100%" stop-color="#FF3E3E"/>
    </linearGradient>
  </defs>
  <polygon points="40,20 135,20 185,75 185,125 135,180 40,180 40,150 55,135 55,65 40,50" fill="url(#dsMetal)" stroke="#8F96A3" stroke-width="3"/>
  <polygon points="135,20 155,5 142,20" fill="#C21818"/>
  <polygon points="185,75 200,68 185,87" fill="#C21818"/>
  <polygon points="185,125 200,132 185,113" fill="#C21818"/>
  <polygon points="135,180 155,195 142,180" fill="#C21818"/>
  <polygon points="80,55 115,55 150,90 150,110 115,145 80,145" fill="#0D0E11" stroke="#B80D18" stroke-width="2"/>
  <g transform="translate(118, 100)">
    <polygon points="0,-42 7,-18 0,-10 -7,-18" fill="url(#dsCrimson)"/>
    <polygon points="0,42 7,18 0,10 -7,18" fill="url(#dsCrimson)"/>
    <polygon points="-35,0 -14,6 -8,0 -14,-6" fill="url(#dsCrimson)"/>
    <polygon points="35,0 14,6 8,0 14,-6" fill="url(#dsCrimson)"/>
    <polygon points="0,-7 7,0 0,7 -7,0" fill="#FFF" stroke="#FF2E2E" stroke-width="1.5"/>
  </g>
</svg>
```

---

## 5. Prohibiciones Estrictas de Diseño
1. **Colores Prohibidos**: No usar verde (#10b981, #22c55e, #00695c), cian (#0284c7), naranja o violeta como colores de marca. El acento es **estrictamente Blutrot (`#C21818`) y Red Glow (`#FF2E2E`)**. Única excepción: el verde y el ámbar como semáforo de estado (`READY`, `MISSING`, `NODE: ONLINE`, alertas de éxito y aviso), nunca aplicados a superficies, bordes de marca ni acentos de navegación.
2. **Deformaciones**: El logotipo e isotipo no deben sufrir escalado no uniforme, deformación axial ni rotación (siempre a 0°).
3. **Fondos**: Prohibido usar fondos blancos o claros para la estructura de la aplicación. Todo es dark mode táctico sobre `#050508`. El lienzo del lector debe permitir negro puro `#000000` para cero fatiga visual.

---

## 6. Implementación en el Frontend (React + MUI v9)

El sistema vive en `frontEnd/src/theme/index.ts`. Trampas reales al tocarlo:

- **Doblar el selector en `Card` y `Dialog`.** Ambos comparten clase con `MuiPaper`, que
  pinta `border: 1px solid #282C38`. Con la misma especificidad gana el último inyectado
  y el borde gris tapa el filo metálico. Los overrides van dentro de `"&&": { ... }`.
- **`MuiAlert` no tiene `standardError`.** MUI 9 fusionó los slots de severidad: ahora es
  el slot `standard` con selectores anidados `&.${alertClasses.colorError}` (y
  `colorSuccess`, `colorWarning`, `colorInfo`). Lo mismo con `Chip`: para no pisar el
  color de un chip de aviso, el gris se limita a `&.${chipClasses.colorDefault}`.
- **El marco HUD es un componente**, `frontEnd/src/components/HudFrame.tsx`. Se pone como
  hijo directo de la tarjeta o del papel del diálogo, antes del contenido.
- **Tokens desde los `sx`**: `import { DS } from "../theme"` en vez de repetir hex sueltos.
- **El tema es dark fijo.** `buildTheme()` no acepta argumentos y `getPalette()` devuelve
  siempre `mode: "dark"`, porque el brandbook prohíbe fondos claros para la estructura de
  la aplicación.
