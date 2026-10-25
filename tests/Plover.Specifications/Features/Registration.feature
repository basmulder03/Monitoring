Feature: Registration and health
  The monitoring system learns about applications by itself and shows how they are doing.

  Scenario: Applications that start are shown as healthy
    Given the environment is running
    Then the dashboard shows the service "orders" in the scope "shop" as Healthy within 60 seconds
    And the dashboard shows the service "payments" in the scope "shop" as Healthy within 60 seconds

  Scenario: An application that breaks is shown as unhealthy, and as healthy again when it is repaired
    Given the environment is running
    And the dashboard shows the service "payments" in the scope "shop" as Healthy within 60 seconds
    When the application "payments" breaks
    Then the dashboard shows the service "payments" in the scope "shop" as Unhealthy within 30 seconds
    And the dashboard shows the service "orders" in the scope "shop" as Healthy within 30 seconds
    When the application "payments" is repaired
    Then the dashboard shows the service "payments" in the scope "shop" as Healthy within 30 seconds

  Scenario: Each pod is shown down to its containers, with the sidecar told apart from the application
    Given the environment is running
    And the dashboard shows the service "orders" in the scope "shop" as Healthy within 60 seconds
    Then the instance "orders-1" of "orders" in the scope "shop" shows a container "app" with the role App within 30 seconds
    And the instance "orders-1" of "orders" in the scope "shop" shows a container "plover-sidecar" with the role PloverSidecar within 30 seconds

  Scenario: The packages an application uses are known
    Given the environment is running
    And the dashboard shows the service "orders" in the scope "shop" as Healthy within 60 seconds
    Then the packages of the applications include "Polly.Core" within 60 seconds
