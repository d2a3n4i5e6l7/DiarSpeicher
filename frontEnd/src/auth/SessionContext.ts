import { createContext, useContext } from "react";

export interface SessionUser {
	id: number;
	username: string;
	role: string;
	is_admin: boolean;
}

export interface SessionValue {
	user: SessionUser | null;
	loading: boolean;
	login: (username: string, password: string) => Promise<void>;
	logout: () => Promise<void>;
	refreshUser: () => Promise<void>;
}

export const SessionContext = createContext<SessionValue>({
	user: null,
	loading: true,
	login: async () => {},
	logout: async () => {},
	refreshUser: async () => {},
});

export function useSession(): SessionValue {
	return useContext(SessionContext);
}
