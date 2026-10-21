import { createBrowserRouter } from 'react-router'
import { PublicLayout } from '../layout/PublicLayout.tsx'
import { HomePage } from './HomePage.tsx'
import { LoginPage } from './LoginPage.tsx'
import { NotFoundPage } from './NotFoundPage.tsx'
import { AccessDeniedPage } from './AccessDeniedPage.tsx'

// Two route trees (ADR-F004). Everything under /staff is lazy: applicants never download staff code or MSAL.
export const router = createBrowserRouter([
  {
    path: '/',
    Component: PublicLayout,
    children: [{ index: true, Component: HomePage }],
  },
  { path: '/login', Component: LoginPage },
  { path: '/access-denied', Component: AccessDeniedPage },
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
      {
        path: 'access-denied',
        lazy: () =>
          import('./AccessDeniedPage.tsx').then((m) => ({
            Component: m.StaffAccessDeniedPage,
          })),
      },
      {
        path: 'jobs',
        lazy: () =>
          import('./StaffJobsPage.tsx').then((m) => ({
            Component: m.StaffJobsPage,
          })),
      },
    ],
  },
  { path: '*', Component: NotFoundPage },
])
