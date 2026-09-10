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
	created_at?: string;
	must_change_password?: boolean | number;
}

export interface CreateUserPayload {
	username: string;
	password: string;
	role?: string;
	permissions?: string;
	role_id?: number;
	expires_in_days?: number;
	must_change_password?: boolean;
	language?: string;
}

export interface UpdateUserPayload {
	username?: string;
	role?: string;
	permissions?: string;
	is_enabled?: boolean | number;
	new_password?: string;
	newPassword?: string;
	role_id?: number;
	expires_in_days?: number;
	language?: string;
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
		gatewayHttp.put<{ updated: boolean }>(`/users/${id}`, {
			new_password: newPassword,
			newPassword: newPassword,
		}),
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

