# Frontend · DiarSpeicher

SPA en React 19 + TypeScript + MUI 9, servida por Vite. Hoy es un panel de administración
(usuarios, roles, subida TUS); el objetivo es convertirla en el cliente de lectura y gestión
de la biblioteca.

## Estado actual

Verificado contra `frontEnd/src` en el commit b287785. Lo que ya funciona:

| Área                  | Fichero                      | Estado                                                        |
| --------------------- | ---------------------------- | ------------------------------------------------------------- |
| Sesión y login        | `pages/LoginPage.tsx`        | Login, primer admin, cambio de contraseña obligatorio          |
| DPoP                  | `auth/dpop.ts`               | Par de claves en IndexedDB, firma de peticiones                |
| Capa HTTP             | `api/client.ts`              | `http` (DiarSpeicher) y `gatewayHttp` (Gateway), 401 global    |
| Layout                | `layout/AppLayout.tsx`       | Drawer responsive, tema claro/oscuro persistido                |
| Usuarios              | `pages/UsersPage.tsx`        | CRUD completo, permisos, protocolos, restricción de edad       |
| Roles                 | `pages/RolesPage.tsx`        | CRUD sobre datos simulados (`rolesApi` devuelve constantes)    |
| Subida TUS            | `pages/UploadPage.tsx`       | Cola, pausa/reanudación, arrastrar y soltar, progreso por fila |
| Cliente TUS           | `api/tusClient.ts`           | Protocolo completo: creación, HEAD, PATCH por trozos, DELETE   |
| Bibliotecas           | `pages/LibrariesPage.tsx`    | Alta con configuración, edición, borrado y escaneo (fase 1)    |

## Lo que falta

El backend ya expone series, medios, páginas, miniaturas y progreso de lectura
(`/api/v2/**`, ver [04-apis-y-protocolos.md](../backend/04-apis-y-protocolos.md)), pero la
SPA no consume nada de eso: no hay navegación de biblioteca, ni detalle de serie, ni lector.
La subida existe pero vive aislada del resto y crea bibliotecas con un formulario mínimo que
ignora casi toda la `LibraryConfig`.

Los planes por fases recogen únicamente ese trabajo pendiente:

| Fase                                          | Entrega                                                       |
| --------------------------------------------- | ------------------------------------------------------------- |
| [fase-1](fase-1-gestion-bibliotecas.md)       | Bibliotecas como sección propia: alta completa, escaneo, ficha |
| [fase-2](fase-2-navegacion.md)                | Home por secciones y rejilla de series, fuera del drawer       |
| [fase-3](fase-3-detalle-serie.md)             | Ficha de serie con cabecera, metadatos y pestañas              |
| [fase-4](fase-4-lector.md)                    | Lector de páginas y persistencia del progreso                  |
| [fase-5](fase-5-metadata-externa.md)          | Consumo del servicio MangaBaka (contenedor aparte)             |

## Convenciones

- **MUI sin librerías extra.** El proyecto ya carga `@mui/material`, `@mui/icons-material` y
  `@mui/x-data-grid`; no se añaden Tailwind, shadcn ni sistemas de diseño paralelos.
- **Tema oscuro por defecto**, definido en `theme/index.ts`. Toda pantalla nueva debe
  funcionar en ambos modos.
- **Los tipos de la API viven en `api/endpoints.ts`**, junto al cliente que los usa.
- **Validación**: `npx eslint <fichero>` sobre cada fichero tocado, nunca sobre el proyecto.
