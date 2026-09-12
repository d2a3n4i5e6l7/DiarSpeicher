<!-- agents_rules:start -->
# DiarSpeicher - Directrices y Reglas para Agentes

## Reglas de Frontend (React 19 / TypeScript / Material-UI)

Para todo el código desarrollado en `frontEnd/`, se deben seguir estrictamente estas directrices para cumplir con las reglas de ESLint y SonarQube del proyecto:

### 1. Promesas en JSX (`@typescript-eslint/no-misused-promises` y `no-floating-promises`)
* React espera funciones que retornen `void` en manejadores de eventos (`onClick`, `onSubmit`).
* NUNCA pases funciones `async` directamente sin envolver.
* Envuelve siempre con `void`:
  ```tsx
  <form onSubmit={(e) => { void handleSubmit(e); }}>
  <Button onClick={() => { void handleClick(); }}>
  ```
* En `useEffect` o funciones síncronas, cualquier promesa que no se espere con `await` debe prefijarse con `void` (ej. `void init();`).

### 2. Ciclo de Vida de Efectos (`react-hooks/set-state-in-effect`)
* NUNCA dispares `setState` síncrono dentro del cuerpo de un `useEffect`.
* Usa una función `async` interna protegida con bandera `isMounted` para actualizar el estado tras la resolución de llamadas de red:
  ```tsx
  useEffect(() => {
    let isMounted = true;
    const init = async () => {
      try {
        const data = await api.list();
        if (isMounted) setData(data);
      } catch (err: unknown) {
        if (isMounted) setError(err instanceof Error ? err.message : "Error");
      } finally {
        if (isMounted) setLoading(false);
      }
    };
    void init();
    return () => { isMounted = false; };
  }, []);
  ```

### 3. Cero Ternarios Anidados en JSX (SonarQube)
* NUNCA anides ternarios (`a ? b : c ? d : e`) dentro de la estructura JSX.
* Extrae el contenido condicional a una variable independiente (`tableContent`, `mainContent`, etc.) antes de la sentencia `return`.

### 4. Material-UI (MUI) y TypeScript
* Íconos: Usar los nombres exportados exactos de la versión instalada (ej. `DeleteOutlinedIcon`, `CheckCircleIcon`).
* No realizar aserciones de tipo innecesarias en `Select` (`onChange={(e) => setVal(e.target.value)}` sin `as number | ""`).
* No usar asignaciones redundantes (`no-useless-assignment`) antes de bloques `try/catch`.

### 5. Tipos de Eventos en React 19
* `React.FormEvent` está obsoleto en React 19 / `@types/react` 19.x. Usar `React.SyntheticEvent` o `React.SubmitEvent` en los manejadores de formularios.

---

## Identidad Visual y Diseño Exclusivo — Brandbook Oficial (Iron Blood Edition)

Todo desarrollo visual, maquetación, estilos CSS, componentes de MUI y vistas de DiarSpeicher deben regirse exclusivamente por el **Brandbook Oficial (Iron Blood Edition / Diarmund Archive Protocol)**:

### 1. Filosofía y Atmósfera
* **Concepto**: Diarmund + Speicher (almacén). Fusión de alta ingeniería táctica/naval (*Iron Blood*), estética de búnker digital y servidor autohospedado de manga/novelas de alto rendimiento.
* **Tono**: Oscuro profundo, militar-táctico, agresivo, funcional y cinematográfico.

### 2. Paleta Cromática y Gradientes Oficiales
* **`Schwarz Core` (Fondo Primario)**: `#050508` (RGB: 5, 5, 8). Fondo absoluto de la aplicación.
* **`Card Background`**: `#0F1015` / `#0A0B0E`. Superficie de tarjetas y estanterías.
* **`Surface Background`**: `#161821` / `#11131C`. Superficie elevada, diálogos y paneles.
* **`Blutrot` (Rojo Carmesí Oficial)**: `#C21818` (RGB: 194, 24, 24). Color primario de acento y branding.
* **`Red Glow` (Iluminación y Estado Activo)**: `#FF2E2E` / `#FF3E3E`. Bordes activos, luces de estado y resplandores.
* **`Red Dark` (Sombra y Contorno)**: `#660B0B` / `#1C0303` / `#331010`.
* **`Platinum` (Texto Principal)**: `#F0F2F6` / `#FFFFFF`.
* **`Muted` (Texto Secundario y Metadatos)**: `#8E95A5` / `#A3ABB8` / `#636B7C`.
* **Gradientes de Interfaz**:
  * Gradiente Oficial: `linear-gradient(135deg, #C21818 0%, #1A0202 50%, #050508 100%)`
  * Gradiente de Header / Tarjeta activa: `linear-gradient(90deg, #1C0303 0%, #050508 100%)`
* **Lienzo de Lectura**: Negro puro (`#000000` o `#050508`) en la pantalla del lector para evitar la fatiga visual.

### 3. Tipografía Oficial
* **Logotexto y Master Identity**: `'Orbitron'`, sans-serif (pesos 700, 900).
* **Títulos de Sección, Metadatos de Manga y Badges**: `'Rajdhani'`, sans-serif (pesos 600, 700, letter-spacing: 1px-2px, texto en mayúsculas).
* **Códigos, Hashes, IDs de Nodo, Contadores y Horas**: `'JetBrains Mono'`, monospace.
* **Cuerpo de Texto, Controles e Interfaz de Lectura**: `'Chakra Petch'` o `'Inter'`, sans-serif.

### 4. Geometría y Elementos Visuales Clave (Key Visuals)
* **Cortes Balísticos a 45°**:
  * Esquinas achaflanadas en tarjetas, botones tácticos y paneles mediante:
    `clip-path: polygon(6px 0%, 100% 0%, 100% calc(100% - 6px), calc(100% - 6px) 100%, 0% 100%, 0% 6px)` o variantes a 45°.
* **Marcadores HUD**:
  * Esquinas tácticas en "L" con borde carmesí de 2px (`border-top: 2px solid #C21818; border-left: 2px solid #C21818`).
* **Botones Tácticos (`btn-tactical`)**:
  * Fondo `#151821`, borde `1px solid #383E4C`, tipografía `Rajdhani` mayúsculas, clip-path a 45°.
  * Hover: Fondo `#C21818`, borde `#FF2E2E`, resplandor `box-shadow: 0 0 14px rgba(229, 9, 20, 0.5)`.
* **Tarjetas de Tomo / Manga**:
  * Proporción vertical 2:3, bordes iluminados con degradado carmesí o `1px solid #282C38`, metadatos en monoespaciado y badges estilo pill `#1B1E26` con borde `#660B0B`.
* **Isotipo Oficial**: Monograma «D» facetado a 45° con la cruz de hierro carmesí central.

### 5. Prohibiciones Estrictas
* **NO usar verdes (#10B981, etc.) ni azules/cianes (#0284C7, #00695c)** como acentos principales. La identidad cromática es rigurosamente *Blutrot / Schwarz Core*.
* **NO deformar ni rotar** el logotipo ni el isotipo (siempre ortogonales a 0°).
* **NO aplicar sombras paralelas anticuadas** o fondos claros / chillones. Todo se construye sobre atmósfera oscura de búnker digital.
<!-- agents_rules:end -->
