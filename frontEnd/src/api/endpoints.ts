import { http, gatewayHttp, API_BASE } from "./client";

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

export const LIBRARY_TYPES = ["Comic", "Manga", "Book", "LightNovel", "Manhwa", "Mixed", "WebNovel", "Webtoon"] as const;
export const LIBRARY_PATTERNS = ["SeriesBased", "CollectionBased"] as const;
export const READING_DIRECTIONS = ["LeftToRight", "RightToLeft"] as const;
export const READING_MODES = ["Paged", "ContinuousVertical", "ContinuousHorizontal"] as const;

/** Espejo de StumpLibraryConfigDto. Los enums viajan como el nombre del miembro, no su indice. */
/**
 * Solo lo que el motor respeta de verdad.
 *
 * `watch`, `hideSeriesView`, `thumbnailWidth/Height` e `ignoreRules` siguen existiendo en
 * la API pero el backend no los consulta en ningún sitio: el vigilante de disco mira todas
 * las bibliotecas siempre, y los otros tres solo se guardaban. Enseñarlos en el formulario
 * prometía un comportamiento que no ocurre, así que no se ofrecen ni se envían.
 */
export interface LibraryConfig {
	libraryType: (typeof LIBRARY_TYPES)[number];
	libraryPattern: (typeof LIBRARY_PATTERNS)[number];
	defaultReadingDir: (typeof READING_DIRECTIONS)[number];
	defaultReadingMode: (typeof READING_MODES)[number];
	defaultLibraryViewMode: "Grid" | "List";
	convertRarToZip: boolean;
	hardDeleteConversions: boolean;
	generateFileHashes: boolean;
	generateKoreaderHashes: boolean;
	processMetadata: boolean;
}

export const DEFAULT_LIBRARY_CONFIG: LibraryConfig = {
	libraryType: "Mixed",
	libraryPattern: "SeriesBased",
	defaultReadingDir: "LeftToRight",
	defaultReadingMode: "Paged",
	defaultLibraryViewMode: "Grid",
	convertRarToZip: false,
	hardDeleteConversions: false,
	generateFileHashes: true,
	generateKoreaderHashes: true,
	processMetadata: true,
};

export interface LibraryItem {
	id: string;
	name: string;
	path: string;
	status?: string;
	seriesCount?: number;
	mediaCount?: number;
	description?: string;
	emoji?: string;
	createdAt?: string;
	updatedAt?: string;
	lastScannedAt?: string;
	config?: LibraryConfig;
}

export const LIBRARY_STATUS_SCANNING = "SCANNING";

export interface CreateLibraryPayload {
	name: string;
	path: string;
	description?: string;
	emoji?: string;
	config?: LibraryConfig;
}

/** Cuerpo de PUT: un campo ausente significa "no tocar". */
export interface UpdateLibraryPayload {
	name?: string;
	description?: string;
	emoji?: string;
	config?: LibraryConfig;
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
	get: (id: string) => http.get<LibraryItem>(`/api/v2/libraries/${id}`),
	create: (payload: CreateLibraryPayload) => http.post<LibraryItem>("/api/v2/libraries", payload),
	update: (id: string, payload: UpdateLibraryPayload) => http.put<LibraryItem>(`/api/v2/libraries/${id}`, payload),
	delete: (id: string) => http.delete<void>(`/api/v2/libraries/${id}`),
	scan: (id: string) => http.post<void>(`/api/v2/libraries/${id}/scan`),
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


// ---------------------------------------------------------------------------
// Catálogo: series y medios (API v2)
// ---------------------------------------------------------------------------

export interface PageResponse<T> {
	data: T[];
	total: number;
	page: number;
	pageSize: number;
	totalPages: number;
}

export interface MediaMetadataItem {
	title?: string;
	summary?: string;
	writers?: string;
	genre?: string;
	publisher?: string;
	ageRating?: number;
	number?: number;
}

export interface MediaItem {
	id: string;
	name: string;
	size: number;
	extension: string;
	pages: number;
	status: string;
	hash?: string;
	koreaderHash?: string;
	path: string;
	seriesId?: string;
	createdAt: string;
	metadata?: MediaMetadataItem;
	/** Última página leída por el usuario actual; ausente si nunca se abrió. */
	currentPage?: number;
	isCompleted: boolean;
}

export interface SeriesMetadataItem {
	/** Origen del dato: "mangabaka" si vino del volcado externo. */
	source?: string;
	externalId?: number;
	title?: string;
	summary?: string;
	publisher?: string;
	writers?: string;
	genres?: string;
	status?: string;
	year?: number;
	coverUrl?: string;
	link?: string;
	/** Último volumen publicado: con él se dice "3 de 12". */
	finalVolume?: number;
	/** Manga, novela, manhwa... según el catálogo externo. */
	type?: string;
	totalChapters?: string;
}

export interface SeriesItem {
	id: string;
	name: string;
	path: string;
	status: string;
	libraryId: string;
	mediaCount: number;
	description?: string;
	/** Marca del último cambio de portada, para romper la caché del navegador. */
	coverUpdatedAt?: string;
	/** Ausente mientras nadie haya emparejado la serie con el catálogo externo. */
	metadata?: SeriesMetadataItem;
}

export interface ProgressPayload {
	page: number;
	percentage?: number;
	isCompleted?: boolean;
}

/** Tamaño de página máximo que acepta el backend (`Math.Clamp(pageSize, 1, 100)`). */
export const MAX_PAGE_SIZE = 100;

function pageQuery(page: number, pageSize: number): string {
	return `page=${String(page)}&pageSize=${String(Math.min(pageSize, MAX_PAGE_SIZE))}`;
}

export const mediaApi = {
	list: (page = 0, pageSize = 20) => http.get<PageResponse<MediaItem>>(`/api/v2/media?${pageQuery(page, pageSize)}`),
	keepReading: () => http.get<MediaItem[]>("/api/v2/media/keep-reading"),
	get: (id: string) => http.get<MediaItem>(`/api/v2/media/${id}`),
	updateProgress: (id: string, payload: ProgressPayload) =>
		http.put<{ updated: boolean }>(`/api/v2/media/${id}/progress`, payload),

	/**
	 * Añadido reciente. El backend ordena `/media` por `Id` y el Id es un ULID, que es
	 * monótono por tiempo de creación: la última página es la más nueva. Sin un
	 * parámetro de orden en la API, esta es la forma de obtenerla sin traerlo todo.
	 */
	latest: async (limit = 20): Promise<MediaItem[]> => {
		const size = Math.min(limit, MAX_PAGE_SIZE);
		const first = await http.get<PageResponse<MediaItem>>(`/api/v2/media?${pageQuery(0, size)}`);
		if (first.totalPages <= 1) {
			return [...first.data].reverse();
		}
		const last = await http.get<PageResponse<MediaItem>>(`/api/v2/media?${pageQuery(first.totalPages - 1, size)}`);
		return [...last.data].reverse();
	},

	// URLs directas: las consume <img> y el navegador manda la cookie de sesión, que
	// es lo que el Gateway resuelve. No pasan por request() porque no llevan prueba DPoP.
	thumbnailUrl: (id: string) => `${API_BASE}/api/v2/media/${id}/thumbnail`,
	pageUrl: (id: string, page: number) => `${API_BASE}/api/v2/media/${id}/page/${String(page)}`,
	fileUrl: (id: string) => `${API_BASE}/api/v2/media/${id}/file`,
};

export const seriesApi = {
	list: (libraryId?: string | null, page = 0, pageSize = 24) => {
		const scope = libraryId ? `&libraryId=${encodeURIComponent(libraryId)}` : "";
		return http.get<PageResponse<SeriesItem>>(`/api/v2/series?${pageQuery(page, pageSize)}${scope}`);
	},
	get: (id: string) => http.get<SeriesItem>(`/api/v2/series/${id}`),
	media: (id: string, page = 0, pageSize = 50) =>
		http.get<PageResponse<MediaItem>>(`/api/v2/series/${id}/media?${pageQuery(page, pageSize)}`),

	/**
	 * El nombre sale de la carpeta, y ahí acaban cosas como "Tomos [01-08][Completo]".
	 * El escáner solo lo escribe al crear la serie, así que renombrar aquí aguanta los
	 * rescaneos. Un campo ausente no se toca.
	 */
	update: (id: string, payload: { name?: string; description?: string }) =>
		http.put<SeriesItem>(`/api/v2/series/${id}`, payload),

	/**
	 * Portada de la serie: la elegida a mano si la hay, y si no la del primer tomo.
	 *
	 * El `token` es la marca de tiempo del último cambio (`metadata.coverUpdatedAt`).
	 * Sin él, el navegador seguiría enseñando la portada vieja desde su caché después de
	 * cambiarla, porque la URL no habría cambiado.
	 */
	thumbnailUrl: (id: string, token?: string | null) =>
		`${API_BASE}/api/v2/series/${id}/thumbnail${token ? `?v=${encodeURIComponent(token)}` : ""}`,

	/** Adopta como portada la miniatura de un tomo ya indexado. */
	setCoverFromMedia: (seriesId: string, mediaId: string) =>
		http.put<{ updated: boolean }>(`/api/v2/series/${seriesId}/thumbnail`, { mediaId }),

	/** Portada subida a mano. JPG, PNG o WebP. */
	uploadCover: (seriesId: string, file: File) => {
		const form = new FormData();
		form.append("file", file);
		return http.upload<{ updated: boolean }>(`/api/v2/series/${seriesId}/thumbnail`, form);
	},

	/** Vuelve a la portada automática: la del primer tomo. */
	clearCover: (seriesId: string) => http.delete<void>(`/api/v2/series/${seriesId}/thumbnail`),
};

// ---------------------------------------------------------------------------
// Metadata externa (volcado de MangaBaka)
// ---------------------------------------------------------------------------

export type MangaBakaState = "Absent" | "Downloading" | "Decompressing" | "Indexing" | "Ready" | "Failed";

export interface MetadataStatus {
	state: MangaBakaState;
	percent?: number | null;
	message?: string | null;
	seriesCount: number;
	sizeBytes: number;
	updatedAt?: string | null;
	busy: boolean;
}

export interface MangaBakaCandidate {
	id: number;
	title: string;
	nativeTitle?: string;
	romanizedTitle?: string;
	type?: string;
	year?: number;
	status?: string;
	coverUrl?: string;
	rating?: number;
	/** Recortada a ~320 caracteres: en la lista solo sirve para reconocer la obra. */
	description?: string;
	authors?: string;
	genres?: string;
}

/** Formatos que acepta la ingesta del volcado. */
export const METADATA_ARCHIVE_EXTENSIONS = [".zst", ".tar.gz", ".tgz"] as const;

export const metadataApi = {
	status: () => http.get<MetadataStatus>("/api/v2/metadata/status"),
	download: () => http.post<void>("/api/v2/metadata/download"),
	import: (file: File) => {
		const form = new FormData();
		form.append("file", file);
		return http.upload<void>("/api/v2/metadata/import", form);
	},
	candidates: (seriesId: string, limit = 10) =>
		http.get<MangaBakaCandidate[]>(`/api/v2/metadata/series/${seriesId}/candidates?limit=${String(limit)}`),
	match: (seriesId: string, mangaBakaId: number) =>
		http.put<{ matched: boolean }>(`/api/v2/metadata/series/${seriesId}/match/${String(mangaBakaId)}`),
	unmatch: (seriesId: string) => http.delete<void>(`/api/v2/metadata/series/${seriesId}/match`),

	/**
	 * Las portadas del catálogo viven en cdn.mangabaka.dev y el navegador las rechaza con
	 * `NS_ERROR_DOM_CORP_FAILED`: son recursos cross-origin que no autorizan su incrustación.
	 * El servidor las trae y las sirve desde nuestro propio origen, con lo que además el
	 * navegador del usuario deja de hablar con un tercero.
	 */
	coverUrl: (externalUrl: string) =>
		`${API_BASE}/api/v2/metadata/cover?url=${encodeURIComponent(externalUrl)}`,
};
