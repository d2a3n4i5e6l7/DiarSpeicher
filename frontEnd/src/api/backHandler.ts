import type { NavigateFunction } from "react-router-dom";

export function backHandler(navigate: NavigateFunction, hasOrigin: boolean) {
	return (e: React.MouseEvent) => {
		const historyState = window.history.state as { idx?: number } | null;
		const hasHistory = typeof historyState?.idx === "number" && historyState.idx > 0;
		if (hasHistory && hasOrigin) {
			e.preventDefault();
			void navigate(-1);
		}
	};
}
