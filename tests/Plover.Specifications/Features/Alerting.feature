Feature: Alerting
  A problem that lasts raises one alert, and the alert goes away when the problem does.

  Scenario: An application that stays unhealthy raises an alert, which ends when it is repaired
    Given the environment is running
    And the dashboard shows the service "orders" in the scope "shop" as Healthy within 60 seconds
    When the application "orders" breaks
    Then the dashboard shows the alert "instance-unhealthy" for "shop/orders" within 60 seconds
    When the application "orders" is repaired
    Then the dashboard shows no alert "instance-unhealthy" within 60 seconds

  Scenario: A problem that lasts raises one alert, not one per check
    Given the environment is running
    And the dashboard shows the service "payments" in the scope "shop" as Healthy within 60 seconds
    When the application "payments" breaks
    Then the dashboard shows the alert "instance-unhealthy" for "shop/payments" within 60 seconds
    And the dashboard shows exactly one alert "instance-unhealthy" for "shop/payments" for the next 15 seconds
