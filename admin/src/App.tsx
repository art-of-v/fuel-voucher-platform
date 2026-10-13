import { QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "sonner";
import AdminScreen from "./pages/admin";
import { queryClient } from "./lib/query-defaults";
import { useTheme } from "./lib/theme-store";
import { themes } from "./lib/themes";

function App() {
  const theme = useTheme((s) => s.theme);
  const isDark = themes[theme].isDark;
  return (
    <QueryClientProvider client={queryClient}>
      <Toaster position="top-center" theme={isDark ? "dark" : "light"} />
      <AdminScreen />
    </QueryClientProvider>
  );
}

export default App;
