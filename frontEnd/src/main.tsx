import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { MemoryRouter } from "react-router-dom";
import App from "./App";
import ErrorBoundary from "./components/ErrorBoundary";

const container = document.getElementById("root");
if (!container) {
	throw new Error("No se encontró el elemento #root en el documento.");
}

createRoot(container).render(
	<StrictMode>
		<ErrorBoundary>
			<MemoryRouter>
				<App />
			</MemoryRouter>
		</ErrorBoundary>
	</StrictMode>
);
