import { Button } from '@fluentui/react-components'
import { PersonRegular } from '@fluentui/react-icons'
import { useNavigate } from 'react-router'
import { AppShell } from './AppShell.tsx'

export function PublicLayout() {
  const navigate = useNavigate()

  return (
    <AppShell
      homeTo="/"
      actions={
        // A link styled as a button: it opens the public sign-in page.
        <Button
          as="a"
          href="/login"
          appearance="primary"
          icon={<PersonRegular />}
          onClick={(event) => {
            event.preventDefault()
            void navigate('/login')
          }}
        >
          Sign in
        </Button>
      }
    />
  )
}
