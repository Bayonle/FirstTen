import {
  Outlet,
  createRootRoute,
  createRoute,
  createRouter,
} from '@tanstack/react-router'
import { AppShell } from './app/AppShell'
import { OperationsHome } from './routes/OperationsHome'
import { IncidentWorkspace } from './routes/IncidentWorkspace'
import { GuidanceWorkspace } from './routes/GuidanceWorkspace'
import { RecognitionWorkspace } from './routes/RecognitionWorkspace'
import { SystemOperations } from './routes/SystemOperations'
import { Login } from './routes/Login'

const rootRoute = createRootRoute({
  component: () => (
    <AppShell>
      <Outlet />
    </AppShell>
  ),
})

const indexRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/',
  component: OperationsHome,
})

const incidentRoute = createRoute({ getParentRoute: () => rootRoute, path: '/incidents/$incidentId', component: IncidentWorkspace })
const guidanceRoute = createRoute({ getParentRoute: () => rootRoute, path: '/guidance', component: GuidanceWorkspace })
const recognitionRoute = createRoute({ getParentRoute: () => rootRoute, path: '/recognition', component: RecognitionWorkspace })
const operationsRoute = createRoute({ getParentRoute: () => rootRoute, path: '/operations', component: SystemOperations })
const loginRoute = createRoute({ getParentRoute: () => rootRoute, path: '/login', component: Login })

const routeTree = rootRoute.addChildren([indexRoute, incidentRoute, guidanceRoute, recognitionRoute, operationsRoute, loginRoute])

export const router = createRouter({
  routeTree,
  defaultPreload: 'intent',
})

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}
