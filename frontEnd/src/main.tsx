import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import App from "./App";

const container = document.getElementById("root");
if (!container) {
	throw new Error("No se encontró el elemento #root en el documento.");
}

createRoot(container).render(
	<StrictMode>
		<App />
	</StrictMode>
);
