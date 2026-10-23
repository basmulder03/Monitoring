Feature: Central configuration
  How agents behave is decided in one place and reaches them without a restart.

  Scenario: A change of the central file reaches the agent, which says it has applied it
    Given the environment is running
    And the dashboard shows the service "orders" in the scope "shop" as Healthy within 60 seconds
    When the central configuration sets pullSeconds to 20
    Then the scope "shop" runs a different configuration version than before within 30 seconds

  Scenario: A file that is not valid changes nothing
    Given the environment is running
    And the dashboard shows the service "orders" in the scope "shop" as Healthy within 60 seconds
    When the central configuration is replaced by a file that is not valid
    Then the scope "shop" still runs the configuration of the valid file within 15 seconds
