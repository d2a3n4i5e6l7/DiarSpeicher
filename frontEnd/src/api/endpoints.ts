import { http, gatewayHttp } from "./client";

export interface LoginPayload {
	username: string;
	password: string;
	client_pubkey?: string;
}

export interface LoginResponse {
	expires_at?: string;
	must_change_password?: boolean;
	requires_first_admin?: boolean;
	message?: string;
	token?: string;
	user?: {
		id: number;
		username: string;
		role: string;
		is_admin: boolean;
	};
}

export interface MeResponse {
	user: {
		id: number;
		username: string;
		role: string;
		is_admin: boolean;
	};
	role: string;
	is_admin: boolean;
	must_change_password?: boolean;
	permissions?: string;
	session?: {
		expires_at: string;
	};
}

export interface UserItem {
	id: number;
	username: string;
	role_id?: number | null;
	role?: string;
	is_admin: boolean;
	is_enabled: boolean | number;
	permissions?: string;
	/** CSV de protocolos de conexion permitidos. Ver PROTOCOL_API_KEY / PROTOCOL_BASIC. */
	allowed_protocols?: string;
	/** Edad del lector. null = sin restriccion de edad. */
	age_restriction?: number | null;
	created_at?: string;
	must_change_password?: boolean | number;
}

/**
 * Cuerpo de POST /plugins/{slug}/auth/users. Los nombres son los del record
 * CreatePluginUserRequest del Gateway: el binder de minimal APIs ignora mayusculas
 * pero no los guiones bajos, asi que `allowed_protocols` no llegaria a enlazarse.
 */
export interface CreateUserPayload {
	username: string;
	password: string;
	role?: string;
	permissions?: string;
	allowedProtocols?: string;
	ageRestriction?: number;
}

/** Cuerpo de PUT /plugins/{slug}/auth/users/{id}. Solo se aplica lo que llega no nulo. */
export interface UpdateUserPayload {
	role?: string;
	permissions?: string;
	isEnabled?: boolean;
	newPassword?: string;
	allowedProtocols?: string;
	ageRestriction?: number;
	/** Pone la edad a NULL. Necesario porque omitir el campo significa "no tocar". */
	clearAgeRestriction?: boolean;
}

export const PROTOCOL_API_KEY = "api_key";
export const PROTOCOL_BASIC = "basic_password";

export interface ProtocolItem {
	id: string;
	label: string;
	description: string;
	sends_credentials_over_network: boolean;
	warning?: string | null;
}

export interface ProtocolCatalog {
	secure_default: string;
	protocols: ProtocolItem[];
}

export interface PermissionItem {
	id: string;
	label: string;
	description: string;
}

export interface PermissionGroup {
	title: string;
	permissions: PermissionItem[];
}

/** Comodin que el Gateway interpreta como "todos los permisos". */
export const PERMISSION_WILDCARD = "*";

/**
 * El Gateway guarda `permissions` como CSV libre y no valida los nombres, asi que el
 * vocabulario lo fijan esta constante y `Permissions.cs` del backend
 * (src/DiarSpeicher.Core/Domain/Models/Permissions.cs), que deben decir lo mismo.
 *
 * Aqui solo aparecen permisos que algun endpoint comprueba de verdad: un interruptor que no
 * apaga nada es peor que no tenerlo, porque el administrador cree haber cerrado una puerta.
 */
export const PERMISSION_GROUPS: PermissionGroup[] = [
	{
		title: "Contenido",
		permissions: [
			{ id: "FileUpload", label: "Subir ficheros", description: "Enviar libros a una biblioteca, por subida directa o por TUS." },
			{ id: "CreateFolder", label: "Crear carpetas", description: "Subir a una subcarpeta que aun no existe. Subir a una que ya existe no lo necesita." },
		],
	},
	{
		title: "Bibliotecas",
		permissions: [
			{ id: "ManageLibrary", label: "Crear bibliotecas", description: "Dar de alta bibliotecas nuevas. Editarlas y borrarlas sigue siendo exclusivo del propietario del servidor." },
			{ id: "ScanLibrary", label: "Lanzar escaneos", description: "Encolar el escaneo de una biblioteca." },
		],
	},
	{
		title: "Clientes de lectura",
		permissions: [
			{ id: "AccessApiKeys", label: "Claves de API", description: "Crear y revocar sus propias claves de dispositivo. Lo comprueba el Gateway, no DiarSpeicher." },
			{ id: "AccessKoreaderSync", label: "Sincronizacion KOReader", description: "Usar el endpoint /koreader para guardar el progreso." },
			{ id: "AccessKoboSync", label: "Sincronizacion Kobo", description: "Usar el endpoint /kobo para sincronizar con el dispositivo." },
		],
	},
];

export const DEFAULT_PERMISSIONS = "AccessApiKeys,AccessKoreaderSync,AccessKoboSync";

export function parseCsv(raw: string | null | undefined): string[] {
	if (!raw) return [];
	return raw
		.split(",")
		.map((entry) => entry.trim())
		.filter((entry) => entry.length > 0);
}

export function hasPermission(permissions: string | null | undefined, id: string): boolean {
	const entries = parseCsv(permissions);
	return entries.includes(PERMISSION_WILDCARD) || entries.some((entry) => entry.toLowerCase() === id.toLowerCase());
}

export function hasProtocol(allowedProtocols: string | null | undefined, id: string): boolean {
	return parseCsv(allowedProtocols).some((entry) => entry.toLowerCase() === id.toLowerCase());
}

export interface RoleItem {
	id: number;
	name: string;
	description?: string | null;
	is_admin: boolean | number;
	user_count?: number;
}

export interface CreateRolePayload {
	name: string;
	description?: string;
	is_admin?: boolean;
}

export interface UpdateRolePayload {
	name?: string;
	description?: string;
	is_admin?: boolean;
}

export interface LibraryItem {
	id: string;
	name: string;
	path: string;
	status?: string;
	seriesCount?: number;
}

export interface CreateLibraryPayload {
	name: string;
	path: string;
	description?: string;
}

export interface UploadedFileItem {
	name: string;
	path: string;
	size: number;
}

export interface UploadResponse {
	uploadedCount: number;
	files: UploadedFileItem[];
	scanJobTriggered: boolean;
}

export const authApi = {
	login: (payload: LoginPayload) => gatewayHttp.post<LoginResponse>("/login", payload),
	registerFirstAdmin: (payload: LoginPayload) => gatewayHttp.post<LoginResponse>("/register-first-admin", payload),
	me: () => gatewayHttp.get<MeResponse>("/me"),
	logout: () => gatewayHttp.post<{ success: boolean }>("/logout"),
	refresh: () => gatewayHttp.post<{ expires_at: string }>("/refresh"),
};

export const usersApi = {
	list: () => gatewayHttp.get<UserItem[]>("/users"),
	create: (payload: CreateUserPayload) => gatewayHttp.post<UserItem>("/users", payload),
	update: (id: number, payload: UpdateUserPayload) => gatewayHttp.put<{ updated: boolean }>(`/users/${id}`, payload),
	delete: (id: number) => gatewayHttp.delete<{ deleted: boolean }>(`/users/${id}`),
	changePassword: (id: number, newPassword: string) =>
		gatewayHttp.put<{ updated: boolean }>(`/users/${id}`, { newPassword }),
	protocols: () => gatewayHttp.get<ProtocolCatalog>("/protocols"),
};

export const rolesApi = {
	list: (): Promise<RoleItem[]> =>
		Promise.resolve([
			{ id: 1, name: "admin", description: "Administrador del reino", is_admin: true, user_count: 1 },
			{ id: 2, name: "reader", description: "Lector estándar", is_admin: false, user_count: 0 },
		]),
	create: (payload: CreateRolePayload): Promise<{ id: number }> =>
		Promise.resolve({ id: payload.name ? 99 : 0 }),
	update: (id: number, payload: UpdateRolePayload): Promise<{ updated: boolean }> =>
		Promise.resolve({ updated: id > 0 && payload !== undefined }),
	delete: (id: number): Promise<void> =>
		id > 0 ? Promise.resolve() : Promise.reject(new Error("ID inválido")),
};

export const librariesApi = {
	list: () => http.get<LibraryItem[]>("/api/v2/libraries"),
	create: (payload: CreateLibraryPayload) => http.post<LibraryItem>("/api/v2/libraries", payload),
	upload: (libraryId: string, files: File[], subpath?: string) => {
		const formData = new FormData();
		if (subpath) {
			formData.append("subpath", subpath);
		}
		for (const file of files) {
			formData.append("files", file);
		}
		return http.upload<UploadResponse>(`/api/v2/libraries/${libraryId}/upload`, formData);
	},
};

export interface SystemClaimResponse {
	isClaimed: boolean;
	status?: string;
}

export const systemApi = {
	getClaimStatus: () => http.get<SystemClaimResponse>("/api/v2/claim"),
};

