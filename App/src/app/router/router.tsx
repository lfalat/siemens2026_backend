import { createBrowserRouter } from 'react-router'
import { PublicLayout } from '../layout/PublicLayout.tsx'
import { HomePage } from './HomePage.tsx'
import { LoginPage } from './LoginPage.tsx'
import { NotFoundPage } from './NotFoundPage.tsx'

// Two route trees (ADR-F004). Everything under /staff is lazy: applicants never download staff code or MSAL.
export const router = createBrowserRouter([
  {
    path: '/',
    Component: PublicLayout,
    children: [{ index: true, Component: HomePage }],
  },
  { path: '/login', Component: LoginPage },
  {
    path: '/staff',
    lazy: () => import('./staffRoute.tsx').then((m) => m.loadStaffRoute()),
    children: [
      {
        index: true,
        lazy: () =>
          import('./StaffHomePage.tsx').then((m) => ({
            Component: m.StaffHomePage,
          })),
      },
    ],
  },
  { path: '*', Component: NotFoundPage },
])
