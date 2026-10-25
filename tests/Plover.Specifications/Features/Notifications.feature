Feature: Notifications
  People who are not looking at the dashboard are told when something starts to be wrong and when it is over, once each.

  Scenario: A webhook is told when an alert starts and when it ends
    Given the environment is running
    And the dashboard shows the service "payments" in the scope "shop" as Healthy within 60 seconds
    When the application "payments" breaks
    Then the webhook is told that the alert "service-down" for "shop/payments" is firing within 60 seconds
    When the application "payments" is repaired
    Then the webhook is told that the alert "service-down" for "shop/payments" is resolved within 60 seconds

  Scenario: A problem that lasts is told once, not on every look
    Given the environment is running
    And the dashboard shows the service "payments" in the scope "shop" as Healthy within 60 seconds
    When the application "payments" breaks
    Then the webhook is told that the alert "service-down" for "shop/payments" is firing within 60 seconds
    And the webhook has been told exactly once that the alert "service-down" for "shop/payments" is firing, for the next 10 seconds
