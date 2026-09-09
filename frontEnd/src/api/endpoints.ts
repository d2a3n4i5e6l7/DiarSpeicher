import { http, gatewayHttp } from "./client";

export interface LoginPayload {
	username: string;
	password: string;
	client_pubkey?: string;
}

export interface LoginResponse {
	expires_at: string;
	must_change_password?: boolean;
	user: {
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
	created_at?: string;
	must_change_password?: boolean | number;
}

export interface CreateUserPayload {
	username: string;
	password: string;
	role_id?: number;
	expires_in_days?: number;
	must_change_password?: boolean;
	language?: string;
}

export interface UpdateUserPayload {
	username?: string;
	role_id?: number;
	is_enabled?: boolean | number;
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
	login: (payload: LoginPayload) => gatewayHttp.post<LoginResponse>("/auth/login", payload),
	me: () => gatewayHttp.get<MeResponse>("/auth/me"),
	logout: () => gatewayHttp.post<{ success: boolean }>("/auth/logout"),
	refresh: () => gatewayHttp.post<{ expires_at: string }>("/auth/refresh"),
};

export const usersApi = {
	list: () => gatewayHttp.get<UserItem[]>("/auth/api/users"),
	create: (payload: CreateUserPayload) => gatewayHttp.post<UserItem>("/auth/api/users", payload),
	update: (id: number, payload: UpdateUserPayload) => gatewayHttp.put<{ updated: boolean }>(`/auth/api/users/${id}`, payload),
	delete: (id: number) => gatewayHttp.delete<void>(`/auth/api/users/${id}`),
	changePassword: (id: number, newPassword: string) =>
		gatewayHttp.post<{ updated: boolean }>(`/auth/api/users/${id}/password`, {
			new_password: newPassword,
			must_change_password: false,
		}),
};

export const rolesApi = {
	list: () => gatewayHttp.get<RoleItem[]>("/auth/api/roles"),
	create: (payload: CreateRolePayload) => gatewayHttp.post<{ id: number }>("/auth/api/roles", payload),
	update: (id: number, payload: UpdateRolePayload) => gatewayHttp.put<{ updated: boolean }>(`/auth/api/roles/${id}`, payload),
	delete: (id: number) => gatewayHttp.delete<void>(`/auth/api/roles/${id}`),
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
