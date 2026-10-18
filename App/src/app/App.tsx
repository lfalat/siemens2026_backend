import '@fontsource-variable/inter'
import { FluentProvider } from '@fluentui/react-components'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from 'react-router'
import { lightTheme } from './providers/theme.ts'
import { router } from './router/router.tsx'

const queryClient = new QueryClient()

export function App() {
  return (
    <FluentProvider theme={lightTheme}>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </FluentProvider>
  )
}
