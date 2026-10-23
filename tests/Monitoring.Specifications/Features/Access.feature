Feature: Access to the dashboard
  Only people and displays that have been given access see the dashboard, and the platform can still tell it is alive.

  Scenario: A visitor without the token is refused, and one with it is let in
    Given the environment is running
    Then a visitor without a token is refused the page "/api/overview"
    And a visitor without a token is refused the page "/tv"
    And a visitor with a wrong token is refused the page "/api/overview"
    And a visitor with the dashboard token is shown the page "/api/overview" and the token is kept in a cookie

  Scenario: The health probe of the platform needs no token
    Given the environment is running
    Then a visitor without a token may ask the health probe "/healthz"

  Scenario: A wall display opens its page with the token and keeps it
    Given the environment is running
    Then a visitor with the dashboard token is shown the page "/tv" and the token is kept in a cookie
