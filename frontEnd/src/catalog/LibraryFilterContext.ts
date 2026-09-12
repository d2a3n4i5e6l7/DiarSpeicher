import { createContext, useContext } from "react";

/** `null` significa "todas las bibliotecas", y es el valor inicial. */
export interface LibraryFilterValue {
	libraryId: string | null;
	setLibraryId: (next: string | null) => void;
}

export const LibraryFilterContext = createContext<LibraryFilterValue>({
	libraryId: null,
	setLibraryId: () => undefined,
});

export const useLibraryFilter = () => useContext(LibraryFilterContext);

export const LIBRARY_FILTER_KEY = "diarspeicher-library-filter";
