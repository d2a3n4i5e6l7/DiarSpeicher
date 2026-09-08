# Fase 2: Integración en Gateway (tvboxHealth) - Panel "Real Apps" y Módulo E-Reader

Este documento define la especificación técnica para la extensión del **Gateway (`tvboxHealth`)** con un panel de control para aplicaciones satélites (**Real Apps**) y el módulo especializado para **E-Reader Real (DiarSpeicher)**.

---

## 1. Concepto de "Real Apps" en el Gateway

El Gateway ya gestiona el perímetro (Nginx, SSL, rate limits, `gateway_session`, tokens JWT ECDSA P-256 y revocación por JTI).
La sección **Real Apps** permite al Gateway actuar como un **IAM Central + API Manager** para servicios satélites:

```
                            ADMIN UI (tvboxHealth)
                                      │
                         ┌────────────┴────────────┐
                         │   Menú: "Real Apps"     │
                         └────────────┬────────────┘
                                      │
                     ┌────────────────┴────────────────┐
                     ▼                                 ▼
             [App: E-Reader Real]             [App: Futuras Apps]
             (DiarSpeicher Backend)          (Audio, Media, etc.)
                     │
         ┌───────────┴───────────┐
         │ - Gestión Usuarios    │
         │ - Generación API Keys │
         │ - Rutas /opds/*       │
         │ - Filtros Parentales  │
         └───────────────────────┘
```

---

## 2. Módulo "E-Reader Real"

Dentro de **Real Apps > E-Reader Real**, el Gateway administra:

### 2.1 Protocolo OPDS y API Keys para Lectores
Los clientes de lectura móvil y tinta electrónica (Panels, Chunky, Moon+ Reader, KOReader) no utilizan formularios web interactivos. Se conectan mediante:
1. **Cabecera HTTP:** `X-Auth-Key: <api_key>`
2. **URL directa:** `/opds/{api_key}/v1.2/catalog` o `/api/v2/opds/...`

**En el Gateway:**
- El administrador puede generar y revocar API Keys asociadas a usuarios concretos para la app *E-Reader*.
- Cuando Nginx recibe una petición con la API Key (por URL o cabecera), el Gateway valida el hash de la clave contra su base de datos.
- Si es válida, Nginx hace el `proxy_pass` hacia DiarSpeicher inyectando:
  ```http
  X-Auth-Sub: usuario123
  X-Auth-Role: reader
  X-Auth-App: ereader
  ```

### 2.2 Sincronización KOReader
- KOReader sincroniza progreso de lectura (`koreader_hash`, página actual, porcentaje).
- El Gateway mapea las rutas `/koreader/...` directamente a DiarSpeicher asegurando que el usuario esté identificado.

---

## 3. Desacoplamiento con DiarSpeicher (Backend)

Con este esquema:
- **El Gateway hace:** Login web, sesiones, passwords, OIDC, generación y validación de API Keys, y filtrado perimetral.
- **DiarSpeicher asume:** Que cualquier petición que llega a su puerto interno ya ha sido autenticada y validada por el Gateway, recibiendo los encabezados de identidad (`X-Auth-Sub`). DiarSpeicher se concentra al 100% en:
  - Escaneo de archivos y caché de `mtime`.
  - Extracción y streaming de cómics/manga/EPUB.
  - Generación del feed XML OPDS v1.2.
  - Almacenamiento del progreso de lectura en su SQLite local (`diarspeicher.db`).
