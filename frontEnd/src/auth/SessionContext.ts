import { createContext, useContext } from "react";
import type { LoginResponse } from "../api/endpoints";

export interface SessionUser {
	id: number;
	username: string;
	role: string;
	is_admin: boolean;
}

export interface SessionValue {
	user: SessionUser | null;
	loading: boolean;
	login: (username: string, password: string) => Promise<LoginResponse | undefined>;
	registerFirstAdmin: (username: string, password: string) => Promise<void>;
	logout: () => Promise<void>;
	refreshUser: () => Promise<void>;
}

export const SessionContext = createContext<SessionValue>({
	user: null,
	loading: true,
	login: () => Promise.resolve(undefined),
	registerFirstAdmin: () => Promise.resolve(),
	logout: () => Promise.resolve(),
	refreshUser: () => Promise.resolve(),
});

export function useSession(): SessionValue {
	return useContext(SessionContext);
}
