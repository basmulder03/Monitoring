Feature: Dependency graph
  The graph of who calls whom is drawn from real traffic, not from configuration.

  Scenario: A call from one application to another shows as a dependency
    Given the environment is running
    And the dashboard shows the service "orders" in the scope "shop" as Healthy within 60 seconds
    When the application "orders" calls the application "payments" 10 times
    Then the dependency graph has a call from "orders" to the address of "payments" within 60 seconds
