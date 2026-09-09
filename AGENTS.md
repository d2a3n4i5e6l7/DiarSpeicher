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
<!-- agents_rules:end -->
